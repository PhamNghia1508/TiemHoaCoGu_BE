using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace HoaSap.DAL.Maintenance;

public sealed record SqliteUpgradeOptions(bool Apply = false, string? BackupPath = null);
public sealed record SqliteUpgradePlan(IReadOnlyList<string> Statements, IReadOnlyList<string> Refusals)
{
    public bool CanApply => Refusals.Count == 0;
    public bool HasChanges => Statements.Count != 0;
    public override string ToString() => Refusals.Count == 0
        ? (Statements.Count == 0 ? "SQLite schema is already current." : string.Join(Environment.NewLine, Statements))
        : $"Refused:{Environment.NewLine}{string.Join(Environment.NewLine, Refusals)}";
}

/// <summary>Offline, additive-only upgrader for databases created by EnsureCreated.</summary>
public static class SqliteSchemaUpgrade
{
    public static async Task<SqliteUpgradePlan> UpgradeAsync(
        DbContextOptions<HoaSapDbContext> options, SqliteUpgradeOptions? upgrade = null,
        CancellationToken cancellationToken = default)
    {
        upgrade ??= new SqliteUpgradeOptions();
        await using var connection = new SqliteConnection(GetConnectionString(options));
        await connection.OpenAsync(cancellationToken);
        return await UpgradeAsync(connection, options, upgrade, cancellationToken);
    }

    public static async Task<SqliteUpgradePlan> UpgradeAsync(
        SqliteConnection target, DbContextOptions<HoaSapDbContext> options, SqliteUpgradeOptions upgrade,
        CancellationToken cancellationToken = default)
    {
        if (!target.State.HasFlag(System.Data.ConnectionState.Open)) await target.OpenAsync(cancellationToken);
        await using var desiredConnection = new SqliteConnection("Data Source=:memory:");
        await desiredConnection.OpenAsync(cancellationToken);
        await using (var desired = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(desiredConnection).Options))
            await desired.Database.EnsureCreatedAsync(cancellationToken);

        var plan = await BuildPlanAsync(target, desiredConnection, cancellationToken);
        if (!upgrade.Apply || (!plan.HasChanges && plan.CanApply)) return plan;
        if (string.IsNullOrWhiteSpace(upgrade.BackupPath))
            throw new InvalidOperationException("Applying a SQLite upgrade requires --backup <offline-copy-path>.");

        var backupPath = Path.GetFullPath(upgrade.BackupPath);
        if (File.Exists(backupPath)) throw new InvalidOperationException("Backup path already exists; choose a new file to preserve previous backups.");
        var dataSource = target.DataSource;
        if (!string.IsNullOrWhiteSpace(dataSource) && dataSource != ":memory:" && Path.GetFullPath(dataSource) == backupPath) throw new InvalidOperationException("Backup must not overwrite the source database.");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await using (var backup = new SqliteConnection($"Data Source={upgrade.BackupPath}"))
        {
            await backup.OpenAsync(cancellationToken);
            target.BackupDatabase(backup);
        }
        if (!plan.CanApply) return plan;

        await using var transaction = (SqliteTransaction)await target.BeginTransactionAsync(cancellationToken);
        var before = await RowCountsAsync(target, cancellationToken);
        try
        {
            foreach (var sql in plan.Statements)
            {
                await using var command = target.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await ValidateAsync(target, cancellationToken);
            var after = await RowCountsAsync(target, cancellationToken);
            foreach (var pair in before)
                if (after.TryGetValue(pair.Key, out var count) && count != pair.Value)
                    throw new InvalidOperationException($"Record count changed for {pair.Key}: {pair.Value} -> {count}.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
        return plan;
    }

    public static async Task<SqliteUpgradePlan> BuildPlanAsync(SqliteConnection target, SqliteConnection desired, CancellationToken ct = default)
    {
        var statements = new List<string>(); var refusals = new List<string>();
        var actualTables = await TablesAsync(target, ct); var desiredTables = await TablesAsync(desired, ct);
        foreach (var table in desiredTables.Keys)
        {
            if (!actualTables.ContainsKey(table))
            {
                statements.Add(await SchemaSqlAsync(desired, table, ct));
                continue;
            }
            var actualColumns = await ColumnsAsync(target, table, ct); var desiredColumns = await ColumnsAsync(desired, table, ct);
            foreach (var column in desiredColumns.Keys)
            {
                if (!actualColumns.ContainsKey(column))
                {
                    var definition = desiredColumns[column];
                    if (definition.Contains(" NOT NULL", StringComparison.OrdinalIgnoreCase) &&
                        !definition.Contains(" DEFAULT ", StringComparison.OrdinalIgnoreCase))
                        refusals.Add($"{table}.{column} is required but has no default; provide a reviewed backfill before adding it.");
                    else
                        statements.Add($"ALTER TABLE {Q(table)} ADD COLUMN {Q(column)} {definition};");
                }
                else if (!EquivalentColumn(actualColumns[column], desiredColumns[column]))
                    refusals.Add($"{table}.{column} differs from the current EF model; refusing rebuild/drop. Repair manually or use a reviewed migration.");
            }
            foreach (var extra in actualColumns.Keys.Except(desiredColumns.Keys, StringComparer.OrdinalIgnoreCase))
                refusals.Add($"{table}.{extra} is not in the current EF model; refusing destructive change.");
            var actualFks = await ForeignKeysAsync(target, table, ct); var desiredFks = await ForeignKeysAsync(desired, table, ct);
            if (!actualFks.SetEquals(desiredFks)) refusals.Add($"{table} foreign keys differ; refusing unsafe constraint change.");
        }
        foreach (var table in actualTables.Keys.Except(desiredTables.Keys, StringComparer.OrdinalIgnoreCase))
            refusals.Add($"Unexpected table {table}; refusing destructive change.");

        var actualIndexes = await IndexesAsync(target, ct); var desiredIndexes = await IndexesAsync(desired, ct);
        foreach (var (name, sql) in desiredIndexes)
            if (!actualIndexes.TryGetValue(name, out var oldSql)) statements.Add(sql + ";");
            else if (!SameSql(oldSql, sql)) refusals.Add($"Index {name} differs; refusing drop/rebuild.");
        foreach (var name in actualIndexes.Keys.Except(desiredIndexes.Keys, StringComparer.OrdinalIgnoreCase))
            refusals.Add($"Unexpected index {name}; refusing destructive change.");
        return new SqliteUpgradePlan(statements, refusals);
    }

    private static async Task ValidateAsync(SqliteConnection c, CancellationToken ct)
    {
        foreach (var pragma in new[] { "PRAGMA foreign_key_check;", "PRAGMA integrity_check;" })
        {
            await using var cmd = c.CreateCommand(); cmd.CommandText = pragma;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && !string.Equals(reader.GetValue(0)?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite validation failed ({pragma}): {reader.GetValue(0)}");
        }
    }

    private static async Task<Dictionary<string, long>> RowCountsAsync(SqliteConnection c, CancellationToken ct)
    { var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); foreach (var table in (await TablesAsync(c, ct)).Keys) { await using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT COUNT(*) FROM {Q(table)}"; result[table] = (long)(await cmd.ExecuteScalarAsync(ct))!; } return result; }
    private static async Task<Dictionary<string, string>> TablesAsync(SqliteConnection c, CancellationToken ct) => await QueryMapAsync(c, "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", ct);
    private static async Task<Dictionary<string, string>> IndexesAsync(SqliteConnection c, CancellationToken ct) => await QueryMapAsync(c, "SELECT name, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL;", ct);
    private static async Task<Dictionary<string, string>> QueryMapAsync(SqliteConnection c, string sql, CancellationToken ct) { var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) d[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1); return d; }
    private static async Task<Dictionary<string, string>> ColumnsAsync(SqliteConnection c, string table, CancellationToken ct) { var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); await using var cmd = c.CreateCommand(); cmd.CommandText = $"PRAGMA table_info({Q(table)});"; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) { var name = r.GetString(1); d[name] = $"{r.GetString(2)}{(r.GetInt32(3) != 0 ? " NOT NULL" : "")}{(r.GetValue(4) is not DBNull ? " DEFAULT " + r.GetValue(4) : "")}"; } return d; }
    private static async Task<HashSet<string>> ForeignKeysAsync(SqliteConnection c, string table, CancellationToken ct) { var d = new HashSet<string>(StringComparer.OrdinalIgnoreCase); await using var cmd = c.CreateCommand(); cmd.CommandText = $"PRAGMA foreign_key_list({Q(table)});"; await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) d.Add(string.Join("|", Enumerable.Range(2, 5).Select(i => r.GetValue(i)?.ToString()))); return d; }
    private static async Task<string> SchemaSqlAsync(SqliteConnection c, string table, CancellationToken ct) { await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$n"; cmd.Parameters.AddWithValue("$n", table); return (string)(await cmd.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException($"Missing schema for {table}")) + ";"; }
    private static string GetConnectionString(DbContextOptions<HoaSapDbContext> options) { using var db = new HoaSapDbContext(options); return db.Database.GetConnectionString() ?? throw new InvalidOperationException("SQLite connection string is required."); }
    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    private static bool EquivalentColumn(string a, string b) => SameSql(a, b);
    private static bool SameSql(string a, string b) => Regex.Replace(a ?? "", "\\s+", " ").Trim().Equals(Regex.Replace(b ?? "", "\\s+", " ").Trim(), StringComparison.OrdinalIgnoreCase);
}
