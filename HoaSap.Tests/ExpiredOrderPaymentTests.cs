using HoaSap.BLL;
using HoaSap.BLL.Workshops;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoaSap.Tests;

public sealed class ExpiredOrderPaymentTests
{
    [Fact]
    public async Task ManualPayment_RejectsExpiredOrderAfterReservedStockWasReleased()
    {
        await using var store = await TestStore.CreateAsync();
        var customer = new Customer { Email = "expired-" + Guid.NewGuid() + "@example.test", PhoneNumber = "0900000000" };
        var category = new Category { Code = "EX-" + Guid.NewGuid().ToString("N"), Name = "Expired" };
        var product = new Product { CategoryId = category.Id, Code = "EX-P-" + Guid.NewGuid().ToString("N"), Name = "Expired product", Price = 100m };
        var order = new Order
        {
            CustomerId = customer.Id,
            OrderCode = "EX-O-" + Guid.NewGuid().ToString("N"),
            SubTotal = 100m,
            TotalAmount = 100m,
            OrderStatus = "Expired",
            PaymentStatus = "Unpaid"
        };
        var item = new OrderItem { OrderId = order.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 100m, FinalPrice = 100m };
        // Expiry already released the reservation: available is restored and reserved is zero.
        var inventory = new Inventory { ProductId = product.Id, QuantityAvailable = 1, QuantityReserved = 0 };
        store.Db.AddRange(customer, category, product, order, item, inventory);
        await store.Db.SaveChangesAsync();

        var service = new WorkshopService(store.Db);
        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.RecordPaymentAsync(
            null, order.Id,
            new ManualPaymentRequest { TransactionCode = "expired-manual-" + Guid.NewGuid().ToString("N"), PaymentMethod = "Cash", Amount = 100m },
            Guid.NewGuid()));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("Expired", await store.Db.Orders.Where(x => x.Id == order.Id).Select(x => x.OrderStatus).SingleAsync());
        Assert.Equal("Unpaid", await store.Db.Orders.Where(x => x.Id == order.Id).Select(x => x.PaymentStatus).SingleAsync());
        Assert.Empty(await store.Db.Payments.Where(x => x.OrderId == order.Id).ToListAsync());
        var savedInventory = await store.Db.Inventories.SingleAsync(x => x.ProductId == product.Id);
        Assert.Equal(1, savedInventory.QuantityAvailable);
        Assert.Equal(0, savedInventory.QuantityReserved);
    }

    [Fact]
    public async Task ManualPayment_StillPaysActiveConfirmedOrder()
    {
        await using var store = await TestStore.CreateAsync();
        var customer = new Customer { Email = "active-" + Guid.NewGuid() + "@example.test", PhoneNumber = "0900000001" };
        var order = new Order
        {
            CustomerId = customer.Id,
            OrderCode = "ACTIVE-O-" + Guid.NewGuid().ToString("N"),
            SubTotal = 100m,
            TotalAmount = 100m,
            OrderStatus = "Confirmed",
            PaymentStatus = "Unpaid"
        };
        store.Db.AddRange(customer, order);
        await store.Db.SaveChangesAsync();

        await new WorkshopService(store.Db).RecordPaymentAsync(
            null, order.Id,
            new ManualPaymentRequest { TransactionCode = "active-manual-" + Guid.NewGuid().ToString("N"), PaymentMethod = "BankTransfer", Amount = 100m },
            Guid.NewGuid());

        Assert.Equal("Paid", await store.Db.Orders.Where(x => x.Id == order.Id).Select(x => x.PaymentStatus).SingleAsync());
        Assert.Single(await store.Db.Payments.Where(x => x.OrderId == order.Id && x.PaymentStatus == "Paid").ToListAsync());
    }

    private sealed class TestStore : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public HoaSapDbContext Db { get; }

        private TestStore(SqliteConnection connection, HoaSapDbContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(connection).Options;
            var db = new HoaSapDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestStore(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
