using System.Text.Json;
using HoaSap.DAL;
using HoaSap.DAL.Auditing;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

public sealed class AuditLogTests
{
    private sealed class TestActor : IAuditActor
    {
        public Guid? ActorId { get; init; }
        public string? Role { get; init; }
        public string? TraceId { get; init; }
    }

    private static (SqliteConnection Connection, HoaSapDbContext Db) OpenDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(connection).Options,
            new TestActor { ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Role = "Admin", TraceId = "trace-test" });
        db.Database.EnsureCreated();
        return (connection, db);
    }

    [Fact]
    public async Task Tracked_business_change_creates_audit_with_safe_whitelist_and_actor()
    {
        var opened = OpenDb();
        await using var db = opened.Db;
        using var connection = opened.Connection;
        var category = new Category { Code = "FLOWER", Name = "Flower" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var product = new Product { CategoryId = category.Id, Code = "ROSE", Name = "Rose", Price = 12.5m, Description = "internal-looking text" };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync(x => x.EntityId == product.Id.ToString());
        Assert.Equal("Product", log.EntityType);
        Assert.Equal("Admin", log.ActorRole);
        Assert.Equal("trace-test", log.TraceId);
        Assert.DoesNotContain("Description", log.NewValuesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", log.NewValuesJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Price", log.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_is_rolled_back_with_business_transaction()
    {
        var opened = OpenDb();
        await using var db = opened.Db;
        using var connection = opened.Connection;
        var category = new Category { Code = "ROLLBACK", Name = "Rollback" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Products.Add(new Product { CategoryId = category.Id, Code = "ROLLBACK-PRODUCT", Name = "Rollback", Price = 10m });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.AuditLogs.CountAsync());
        await transaction.RollbackAsync();
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Audit_update_and_delete_are_rejected()
    {
        var opened = OpenDb();
        await using var db = opened.Db;
        using var connection = opened.Connection;
        var log = new AuditLog { EntityType = "Product", EntityId = Guid.NewGuid().ToString(), Action = "Added" };
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();
        log.Action = "Deleted";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(log).State = EntityState.Unchanged;
        db.AuditLogs.Remove(log);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
