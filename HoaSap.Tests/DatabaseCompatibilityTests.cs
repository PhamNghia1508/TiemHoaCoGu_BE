using HoaSap.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Xunit;

namespace HoaSap.Tests;

public sealed class DatabaseCompatibilityTests
{
    [Fact]
    public async Task NativeSqlite_IsAtLeastPatchedVersion()
    {
        await using var db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var nativeVersion = Version.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.True(nativeVersion >= new Version(3, 50, 2), $"Vulnerable SQLite native version: {nativeVersion}");
    }

    [Fact]
    public void SqlServer_MigrationGeneratesScriptWithoutConnecting()
    {
        using var db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>()
            .UseSqlServer("Server=localhost;Database=SchemaGenerationOnly;Integrated Security=true;TrustServerCertificate=true").Options);
        var script = db.Database.GetService<IMigrator>().GenerateScript();
        Assert.Contains("InitialProduction", script);
        Assert.Contains("CREATE TABLE [Orders]", script);
        Assert.Contains("CK_Session_Capacity", script);
        Assert.Contains("CK_Payment_Amount", script);
        Assert.Contains("IX_Refunds_TransactionCode", script);
        Assert.Contains("AddAuditLogs", script);
        Assert.Contains("CREATE TABLE [AuditLogs]", script);
        Assert.Contains("AddIdentityCmsDefaults", script);
        Assert.Contains("[IsActive]", script);
        Assert.Contains("[ImageUrl]", script);
    }

    [SqlServerFact]
    public async Task SqlServer_MigrationRoundTrip_IsOptIn()
    {
        var configured = Environment.GetEnvironmentVariable("HOASAP_TEST_SQLSERVER")!;

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"HoaSapTest_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var db = new HoaSapDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261022092453_AddAuditLogs");
            var legacyAccount = Guid.NewGuid();
            var legacyCategory = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Customers] ([Id],[FullName],[PhoneNumber],[Email],[PasswordHash],[Role],[IsGuest],[CreatedAt]) VALUES ({legacyAccount},{"Legacy Admin"},{"0909876543"},{"legacy@example.test"},{"not-a-real-password-hash"},{"Admin"},{false},{DateTime.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Categories] ([Id],[Code],[Name],[Description]) VALUES ({legacyCategory},{"LEGACY"},{"Existing Category"},{"Preserved"})");
            await db.Database.MigrateAsync();
            Assert.True(await db.Database.CanConnectAsync());
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, migration => migration.Contains("InitialProduction", StringComparison.Ordinal));
            Assert.Contains(applied, migration => migration.Contains("AddAuditLogs", StringComparison.Ordinal));
            Assert.Contains(applied, migration => migration.Contains("AddIdentityCmsDefaults", StringComparison.Ordinal));
            Assert.True((await db.Customers.SingleAsync(x => x.Id == legacyAccount)).IsActive);
            Assert.Equal("", (await db.Categories.SingleAsync(x => x.Id == legacyCategory)).ImageUrl);
            Assert.Equal(0, await db.AuditLogs.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOASAP_TEST_SQLSERVER")))
            Skip = "Chưa cấu hình SQL Server thực: đặt HOASAP_TEST_SQLSERVER để chạy kiểm thử tích hợp.";
    }
}
