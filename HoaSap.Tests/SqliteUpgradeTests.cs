using HoaSap.DAL;
using HoaSap.DAL.Maintenance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

public sealed class SqliteUpgradeTests
{
    [Fact]
    public async Task Apply_is_additive_idempotent_and_preserves_existing_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "hoasap-upgrade-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var db = Path.Combine(root, "legacy.db"); var backup = Path.Combine(root, "before.db");
        var options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite($"Data Source={db}").Options;
        await using (var dbContext = new HoaSapDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Customers.Add(new HoaSap.Domain.Entities.Customer { Id = Guid.NewGuid(), FullName = "Legacy customer", Email = "legacy@example.test", PhoneNumber = "0900000000", IsActive = true });
            await dbContext.SaveChangesAsync();
        }
        await using (var c = new SqliteConnection($"Data Source={db}"))
        { await c.OpenAsync(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL LIMIT 1"; var index = (string?)await cmd.ExecuteScalarAsync(); if (index is not null) { cmd.CommandText = $"DROP INDEX \"{index.Replace("\"", "\"\"")}\""; await cmd.ExecuteNonQueryAsync(); } }

        var first = await SqliteSchemaUpgrade.UpgradeAsync(options, new SqliteUpgradeOptions(true, backup));
        Assert.True(first.CanApply); Assert.True(File.Exists(backup));
        var second = await SqliteSchemaUpgrade.UpgradeAsync(options, new SqliteUpgradeOptions(true, Path.Combine(root, "before-second.db")));
        Assert.True(second.CanApply); Assert.False(second.HasChanges);
        await using (var verify = new HoaSapDbContext(options)) Assert.Equal(1, await verify.Customers.CountAsync());
    }

    [Fact]
    public async Task Incompatible_schema_is_refused_and_backup_is_created_without_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "hoasap-refuse-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var db = Path.Combine(root, "legacy.db"); var backup = Path.Combine(root, "before.db");
        await using (var c = new SqliteConnection($"Data Source={db}"))
        {
            await c.OpenAsync(); await using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE Customers (Id TEXT PRIMARY KEY, Email INTEGER NOT NULL); INSERT INTO Customers VALUES ('1', 7);"; await cmd.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite($"Data Source={db}").Options;
        var plan = await SqliteSchemaUpgrade.UpgradeAsync(options, new SqliteUpgradeOptions(true, backup));
        Assert.False(plan.CanApply); Assert.Contains(plan.Refusals, x => x.Contains("Customers.Email", StringComparison.Ordinal)); Assert.True(File.Exists(backup));
        await using var check = new SqliteConnection($"Data Source={db}"); await check.OpenAsync(); await using var verify = check.CreateCommand(); verify.CommandText = "SELECT COUNT(*) FROM Customers"; Assert.Equal(1L, (long)(await verify.ExecuteScalarAsync())!);
    }
}
