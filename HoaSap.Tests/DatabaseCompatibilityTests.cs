using HoaSap.DAL;
using Microsoft.EntityFrameworkCore;
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
    public void SqlServer_ModelGeneratesSchemaWithoutConnecting()
    {
        using var db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>()
            .UseSqlServer("Server=localhost;Database=SchemaGenerationOnly;Integrated Security=true;TrustServerCertificate=true").Options);
        var schema = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE [Orders]", schema);
        Assert.Contains("CK_Session_Capacity", schema);
        Assert.Contains("CK_Payment_Amount", schema);
    }
}
