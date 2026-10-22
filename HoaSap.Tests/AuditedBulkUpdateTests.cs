using System.Data;
using System.Text.Json;
using HoaSap.DAL;
using HoaSap.DAL.Auditing;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

public sealed class AuditedBulkUpdateTests
{
    private sealed class Actor : IAuditActor
    {
        public Guid? ActorId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public string? Role => "Admin";
        public string? TraceId => "bulk-unit-trace";
    }

    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public HoaSapDbContext Db { get; }
        public Inventory Inventory { get; }
        public Product Product { get; }
        public DbContextOptions<HoaSapDbContext> Options { get; }

        public Store()
        {
            Connection.Open();
            Options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(Connection).Options;
            Db = new HoaSapDbContext(Options, new Actor());
            Db.Database.EnsureCreated();
            var category = new Category { Code = "BULK-C", Name = "Bulk" };
            Product = new Product { CategoryId = category.Id, Code = "BULK-P", Name = "Bulk", Price = 100m };
            Inventory = new Inventory { ProductId = Product.Id, QuantityAvailable = 5 };
            Db.AddRange(category, Product, Inventory);
            Db.SaveChanges();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static Task<int> ReserveAsync(Store store)
    {
        var token = Guid.NewGuid();
        return store.Db.ExecuteUpdateAuditedAsync(
            store.Db.Inventories.Where(x => x.Id == store.Inventory.Id && x.QuantityAvailable >= 2),
            s => s.SetProperty(x => x.QuantityAvailable, x => x.QuantityAvailable - 2)
                .SetProperty(x => x.QuantityReserved, x => x.QuantityReserved + 2)
                .SetProperty(x => x.ConcurrencyToken, token));
    }

    [Fact]
    public async Task BulkAudit_RequiresActiveTransactionBeforeAnyMutation()
    {
        await using var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(store));
        Assert.Equal(5, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        Assert.Equal(0, await store.Db.AuditLogs.CountAsync(x => x.Action == "Modified"));
    }

    [Fact]
    public async Task BulkAudit_RejectsUnconfiguredEntityBeforeMutation()
    {
        await using var store = new Store();
        await using var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Db.ExecuteUpdateAuditedAsync(
            store.Db.Categories, s => s.SetProperty(x => x.Name, "Changed")));
        Assert.Equal("Bulk", await store.Db.Categories.Select(x => x.Name).SingleAsync());
    }

    [Fact]
    public async Task BulkAudit_RejectsPendingTrackedChangesAndRefreshesUnchangedSnapshots()
    {
        await using var store = new Store();
        await using var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        store.Inventory.QuantityAvailable = 100;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(store));
        Assert.Equal(5, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        store.Db.Entry(store.Inventory).State = EntityState.Unchanged;
        Assert.Equal(1, await ReserveAsync(store));
        Assert.Equal(EntityState.Detached, store.Db.Entry(store.Inventory).State);
        await store.Db.SaveChangesAsync();
        await tx.CommitAsync();
        var log = await store.Db.AuditLogs.SingleAsync(x => x.Action == "Modified");
        using var before = JsonDocument.Parse(log.OldValuesJson);
        using var after = JsonDocument.Parse(log.NewValuesJson);
        Assert.Equal(5, before.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(3, after.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(0, before.RootElement.GetProperty("QuantityReserved").GetInt32());
        Assert.Equal(2, after.RootElement.GetProperty("QuantityReserved").GetInt32());
        Assert.Equal(new Actor().ActorId, log.ActorId);
        Assert.Equal("Admin", log.ActorRole);
        Assert.Equal("bulk-unit-trace", log.TraceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkAudit_UnsavedRollbackDoesNotLeakIntoReusedContext(bool synchronousSave)
    {
        await using var store = new Store();
        await using (var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.Equal(1, await ReserveAsync(store));
            await tx.RollbackAsync();
        }
        if (synchronousSave) store.Db.SaveChanges();
        else await store.Db.SaveChangesAsync();
        Assert.Equal(5, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        Assert.Equal(0, await store.Db.AuditLogs.CountAsync(x => x.Action == "Modified"));
        await using var next = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(1, await ReserveAsync(store));
        await store.Db.SaveChangesAsync();
        await next.CommitAsync();
        Assert.Equal(1, await store.Db.AuditLogs.CountAsync(x => x.Action == "Modified"));
    }

    [Fact]
    public async Task BulkAudit_SaveChangesRetryKeepsBulkLogButDoesNotDuplicateTrackedLogs()
    {
        await using var store = new Store();
        await using var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(1, await ReserveAsync(store));
        var duplicate = new Product { CategoryId = store.Product.CategoryId, Code = store.Product.Code, Name = "Duplicate", Price = 1m };
        store.Db.Products.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
        store.Db.Entry(duplicate).State = EntityState.Detached;
        await store.Db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(3, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        Assert.Equal(1, await store.Db.AuditLogs.CountAsync(x => x.EntityType == "Inventory" && x.Action == "Modified"));
        Assert.False(await store.Db.AuditLogs.AnyAsync(x => x.EntityId == duplicate.Id.ToString()));
    }

    [Fact]
    public async Task BulkAudit_RepeatedUpdatesChainSnapshotsAndZeroMatchesAddNoLog()
    {
        await using var store = new Store();
        await using var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(1, await ReserveAsync(store));
        Assert.Equal(1, await ReserveAsync(store));
        Assert.Equal(0, await ReserveAsync(store));
        await store.Db.SaveChangesAsync();
        await tx.CommitAsync();
        var logs = await store.Db.AuditLogs.Where(x => x.Action == "Modified")
            .OrderBy(x => x.OccurredAtUtc).ToListAsync();
        Assert.Equal(2, logs.Count);
        using var firstBefore = JsonDocument.Parse(logs[0].OldValuesJson);
        using var firstAfter = JsonDocument.Parse(logs[0].NewValuesJson);
        using var secondBefore = JsonDocument.Parse(logs[1].OldValuesJson);
        using var secondAfter = JsonDocument.Parse(logs[1].NewValuesJson);
        Assert.Equal(5, firstBefore.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(3, firstAfter.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(3, secondBefore.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(1, secondAfter.RootElement.GetProperty("QuantityAvailable").GetInt32());
        Assert.Equal(4, secondAfter.RootElement.GetProperty("QuantityReserved").GetInt32());
        Assert.DoesNotContain("ConcurrencyToken", logs[0].NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkAudit_ChangesTokenSoStaleContextCannotOverwriteInventory()
    {
        await using var store = new Store();
        await using var stale = new HoaSapDbContext(store.Options);
        var inventory = await stale.Inventories.SingleAsync();
        var oldToken = inventory.ConcurrencyToken;
        await using (var tx = await store.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.Equal(1, await ReserveAsync(store));
            await store.Db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        Assert.NotEqual(oldToken, await store.Db.Inventories.Select(x => x.ConcurrencyToken).SingleAsync());
        inventory.QuantityAvailable = 99;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(3, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        Assert.Equal(1, await store.Db.AuditLogs.CountAsync(x => x.EntityType == "Inventory" && x.Action == "Modified"));
    }
}
