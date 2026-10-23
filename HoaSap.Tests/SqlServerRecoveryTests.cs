using System.Data;
using System.Security.Cryptography;
using HoaSap.DAL;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

/// <summary>
/// Local-only, non-destructive recovery drill. It never uses the destructive
/// production restore script and always restores into a fresh database name.
/// </summary>
public sealed class SqlServerRecoveryTests
{
    [SqlServerFact]
    public async Task FullBackupRestore_ToDistinctDatabase_PreservesSchemaMigrationsAuditRowsMediaAndDataProtection()
    {
        var configured = Environment.GetEnvironmentVariable("HOASAP_TEST_SQLSERVER")!;
        var sourceName = $"HoaSapRecoverySource_{Guid.NewGuid():N}";
        var restoredName = $"HoaSapRecoveryRestored_{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), $"hoasap-recovery-{Guid.NewGuid():N}");
        // SQL Server may be in a CI service container: backup/MOVE paths belong
        // to the server filesystem, not the test runner's /tmp directory.
        var serverDataPath = await ReadServerDataPathAsync(configured);
        var backupPath = serverDataPath + sourceName + ".bak";
        var mediaPath = Path.Combine(root, "media");
        var restoredMediaPath = Path.Combine(root, "media-restored");
        var sourceKeys = Path.Combine(root, "keys-source");
        var restoredKeys = Path.Combine(root, "keys-restored");
        Directory.CreateDirectory(mediaPath);
        Directory.CreateDirectory(sourceKeys);

        var sourceBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = sourceName };
        var restoredBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = restoredName };
        var sourceConnection = sourceBuilder.ConnectionString;
        var restoredConnection = restoredBuilder.ConnectionString;
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var mediaBytes = RandomNumberGenerator.GetBytes(4096);
        var mediaFile = Path.Combine(mediaPath, "recovery-sample.bin");
        await File.WriteAllBytesAsync(mediaFile, mediaBytes);
        var expectedMediaHash = Convert.ToHexString(SHA256.HashData(mediaBytes));
        var originalToken = "recovery-token-" + Guid.NewGuid().ToString("N");
        var protectedToken = originalToken;

        try
        {
            await using (var source = CreateContext(sourceConnection))
            {
                await source.Database.MigrateAsync();
                source.Categories.Add(new Category { Id = categoryId, Code = "RECOVERY", Name = "Recovery sample", Description = "local drill" });
                source.Products.Add(new Product { Id = productId, CategoryId = categoryId, Code = "RECOVERY-FLOWER", Name = "Recovery flower", Price = 123.45m, Description = "backup sample" });
                source.Customers.Add(new Customer { Id = customerId, FullName = "Recovery Customer", PhoneNumber = "0900000009", Email = $"recovery-{customerId:N}@example.test", PasswordHash = "test-only", Role = "Customer" });
                source.AuditLogs.Add(new AuditLog { Id = auditId, EntityType = "Product", EntityId = productId.ToString(), Action = "Added", OldValuesJson = "{}", NewValuesJson = "{\"Code\":\"RECOVERY-FLOWER\"}", ActorRole = "LocalRecoveryDrill", TraceId = "recovery-drill" });
                await source.SaveChangesAsync();
            }

            var provider = DataProtectionProvider.Create(new DirectoryInfo(sourceKeys), cfg => cfg.SetApplicationName("HoaSap"));
            using (provider as IDisposable)
            {
                protectedToken = provider.CreateProtector("recovery-drill").Protect(protectedToken);
            }

            await ExecuteMasterAsync(configured, $"BACKUP DATABASE {QuoteIdentifier(sourceName)} TO DISK = N'{QuoteLiteral(backupPath)}' WITH INIT, CHECKSUM, FORMAT");
            var files = await ReadBackupFilesAsync(configured, backupPath);
            Assert.NotEmpty(files);

            var moveClauses = files.Select((file, index) =>
            {
                var extension = file.Type.Equals("L", StringComparison.OrdinalIgnoreCase) ? "ldf" : "mdf";
                var target = serverDataPath + $"{restoredName}_{index}.{extension}";
                return $"MOVE N'{QuoteLiteral(file.LogicalName)}' TO N'{QuoteLiteral(target)}'";
            });
            await ExecuteMasterAsync(configured, $"RESTORE DATABASE {QuoteIdentifier(restoredName)} FROM DISK = N'{QuoteLiteral(backupPath)}' WITH {string.Join(", ", moveClauses)}, RECOVERY, CHECKSUM");

            await using (var restored = CreateContext(restoredConnection))
            {
                Assert.True(await restored.Database.CanConnectAsync());
                await restored.Database.MigrateAsync(); // proves the restored schema remains migration-compatible.
                var migrations = await restored.Database.GetAppliedMigrationsAsync();
                Assert.Contains(migrations, x => x.Contains("InitialProduction", StringComparison.Ordinal));
                Assert.Contains(migrations, x => x.Contains("AddAuditLogs", StringComparison.Ordinal));
                Assert.Contains(migrations, x => x.Contains("CompleteCommerceLifecycle", StringComparison.Ordinal));
                Assert.Equal("Recovery flower", await restored.Products.Where(x => x.Id == productId).Select(x => x.Name).SingleAsync());
                Assert.Equal("Recovery sample", await restored.Categories.Where(x => x.Id == categoryId).Select(x => x.Name).SingleAsync());
                Assert.Equal("Recovery Customer", await restored.Customers.Where(x => x.Id == customerId).Select(x => x.FullName).SingleAsync());
                var audit = await restored.AuditLogs.SingleAsync(x => x.Id == auditId);
                Assert.Equal("Product", audit.EntityType);
                Assert.Equal("recovery-drill", audit.TraceId);
            }

            DirectoryCopy(sourceKeys, restoredKeys);
            DirectoryCopy(mediaPath, restoredMediaPath);
            var restoredProvider = DataProtectionProvider.Create(new DirectoryInfo(restoredKeys), cfg => cfg.SetApplicationName("HoaSap"));
            using (restoredProvider as IDisposable)
            {
                var plaintext = restoredProvider.CreateProtector("recovery-drill").Unprotect(protectedToken);
                Assert.Equal(originalToken, plaintext);
            }
            Assert.Equal(expectedMediaHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(restoredMediaPath, Path.GetFileName(mediaFile))))));
        }
        finally
        {
            await TryDropDatabaseAsync(configured, sourceName);
            await TryDropDatabaseAsync(configured, restoredName);
            TryDeleteDirectory(root);
            // Local engine: remove backup directly. In CI it lives only for the
            // disposable SQL service container lifetime; never delete remote paths.
            TryDeleteFile(backupPath);
        }
    }

    private static HoaSapDbContext CreateContext(string connection) => new(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlServer(connection).Options);

    private static async Task<string> ReadServerDataPathAsync(string configured)
    {
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))", connection);
        var path = (string?)await command.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path!.TrimEnd('/', '\\') + (path.Contains('\\') ? "\\" : "/");
    }

    private static async Task ExecuteMasterAsync(string configured, string sql)
    {
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private sealed record BackupFile(string LogicalName, string Type);

    private static async Task<List<BackupFile>> ReadBackupFilesAsync(string configured, string backupPath)
    {
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{QuoteLiteral(backupPath)}'", connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<BackupFile>();
        while (await reader.ReadAsync()) result.Add(new BackupFile(reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
        return result;
    }

    private static async Task TryDropDatabaseAsync(string configured, string database)
    {
        try { await ExecuteMasterAsync(configured, $"IF DB_ID(N'{QuoteLiteral(database)}') IS NOT NULL ALTER DATABASE {QuoteIdentifier(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; IF DB_ID(N'{QuoteLiteral(database)}') IS NOT NULL DROP DATABASE {QuoteIdentifier(database)}"); }
        catch { /* cleanup must not mask the drill failure */ }
    }

    private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private static string QuoteLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void DirectoryCopy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
