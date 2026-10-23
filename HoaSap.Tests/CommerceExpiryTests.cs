using HoaSap.BLL.Commerce;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

public sealed class CommerceExpiryTests
{
    [Fact]
    public async Task ExpireUnpaid_releases_reserved_inventory_and_voucher_once_and_replay_is_noop()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var category = new Category { Code = "C-" + Guid.NewGuid().ToString("N"), Name = "Test" }; var product = new Product { CategoryId = category.Id, Name = "Rose", Code = "P-" + Guid.NewGuid().ToString("N"), Price = 80m };
        var inventory = new Inventory { ProductId = product.Id, QuantityAvailable = 2, QuantityReserved = 3 };
        var voucher = new Voucher { Code = "V-" + Guid.NewGuid().ToString("N"), Description = "test", DiscountType = "FixedAmount", DiscountValue = 5m, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1), UsageLimit = 10, UsedCount = 1, IsActive = true };
        var order = NewOrder(owner, product.Id, voucher.Id, DateTime.UtcNow.AddMinutes(-31));
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Categories.Add(category); store.Db.Products.Add(product); store.Db.Inventories.Add(inventory); store.Db.Vouchers.Add(voucher); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var service = new CommerceService(store.Db);

        Assert.Equal(1, await service.ExpireUnpaidOrdersAsync(order.CreatedAt.AddMinutes(30)));
        Assert.Equal(0, await service.ExpireUnpaidOrdersAsync(order.CreatedAt.AddMinutes(30)));
        var savedInventory = await store.Db.Inventories.SingleAsync(); var savedVoucher = await store.Db.Vouchers.SingleAsync(); var savedOrder = await store.Db.Orders.SingleAsync();
        Assert.Equal(0, savedInventory.QuantityReserved); Assert.Equal(5, savedInventory.QuantityAvailable);
        await service.CancelAsync(owner, order.Id);
        Assert.Equal(0, savedVoucher.UsedCount); Assert.Equal("Expired", savedOrder.OrderStatus);
    }

    [Fact]
    public async Task ExpireUnpaid_does_not_expire_paid_orders_or_release_their_holds()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var category = new Category { Code = "C-" + Guid.NewGuid().ToString("N"), Name = "Test" }; var product = new Product { CategoryId = category.Id, Name = "Paid Rose", Code = "P-" + Guid.NewGuid().ToString("N"), Price = 20m };
        var inventory = new Inventory { ProductId = product.Id, QuantityAvailable = 0, QuantityReserved = 1 };
        var order = NewOrder(owner, product.Id, null, DateTime.UtcNow.AddMinutes(-45)); order.PaymentStatus = "Paid";
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Categories.Add(category); store.Db.Products.Add(product); store.Db.Inventories.Add(inventory); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var service = new CommerceService(store.Db);
        Assert.Equal(0, await service.ExpireUnpaidOrdersAsync(DateTime.UtcNow));
        Assert.Equal(1, (await store.Db.Inventories.SingleAsync()).QuantityReserved); Assert.Equal("Confirmed", (await store.Db.Orders.SingleAsync()).OrderStatus);
    }

    [Fact]
    public async Task ExpireUnpaid_custom_order_skips_inventory_release_but_expires_order()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var category = new Category { Code = "C-" + Guid.NewGuid().ToString("N"), Name = "Test" }; var product = new Product { CategoryId = category.Id, Name = "Custom Rose", Code = "P-" + Guid.NewGuid().ToString("N"), Price = 20m };
        var inventory = new Inventory { ProductId = product.Id, QuantityAvailable = 0, QuantityReserved = 2 };
        var order = NewOrder(owner, product.Id, null, DateTime.UtcNow.AddMinutes(-31));
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Categories.Add(category); store.Db.Products.Add(product); store.Db.Inventories.Add(inventory); store.Db.Orders.Add(order);
        var request = new CustomOrderRequest { CustomerId = owner, ProductId = product.Id, Quantity = 2 }; var quotation = new CustomQuotation { CustomOrderRequestId = request.Id, CurrentRevision = 1 }; store.Db.Add(request); store.Db.Add(quotation);
        store.Db.Set<CustomOrderCheckout>().Add(new CustomOrderCheckout { OrderId = order.Id, CustomerId = owner, IdempotencyKey = "custom-1", CustomQuotationId = quotation.Id, QuotationVersion = 1 });
        await store.Db.SaveChangesAsync();
        var service = new CommerceService(store.Db);
        Assert.Equal(1, await service.ExpireUnpaidOrdersAsync(DateTime.UtcNow));
        Assert.Equal(2, (await store.Db.Inventories.SingleAsync()).QuantityReserved); Assert.Equal("Expired", (await store.Db.Orders.SingleAsync()).OrderStatus);
    }

    static Order NewOrder(Guid owner, Guid productId, Guid? voucherId, DateTime created) => new() { CustomerId = owner, VoucherId = voucherId, TotalAmount = 80m, SubTotal = 80m, OrderCode = "ORD-" + Guid.NewGuid().ToString("N"), OrderStatus = "Confirmed", PaymentStatus = "Unpaid", CreatedAt = created, Items = [new OrderItem { ProductId = productId, Quantity = 3, UnitPrice = 20m, FinalPrice = 60m }] };
    sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:"); public HoaSapDbContext Db { get; private set; } = null!;
        public static async Task<Store> CreateAsync() { var s = new Store(); await s.Connection.OpenAsync(); s.Db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(s.Connection).Options); await s.Db.Database.EnsureCreatedAsync(); return s; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
