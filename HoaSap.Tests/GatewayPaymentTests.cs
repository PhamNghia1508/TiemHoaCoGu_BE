using System.Text;
using System.Text.Json;
using HoaSap.BLL;
using HoaSap.BLL.Commerce;
using HoaSap.DAL;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoaSap.Tests;

public sealed class GatewayPaymentTests
{
    [Fact]
    public async Task Start_is_durable_and_provider_is_called_once_on_replay()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 100m);
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway { ChargeResult = new(true, "tx-1", "https://pay.example/1", "raw") };
        var service = new GatewayPaymentService(store.Db, gateway, Options());

        var first = await service.StartAsync(owner, new() { OrderId = order.Id, Amount = 100m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-1" });
        var second = await service.StartAsync(owner, new() { OrderId = order.Id, Amount = 100m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-1" });

        Assert.Equal(first.TransactionCode, second.TransactionCode);
        Assert.Equal(1, gateway.ChargeCalls);
        Assert.Single(store.Db.GatewayPaymentIntents);
        Assert.Single(store.Db.Payments);
    }

    [Fact]
    public async Task Unknown_charge_is_durable_and_never_automatically_retried()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 50m);
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway { ChargeException = new InvalidOperationException("timeout") };
        var service = new GatewayPaymentService(store.Db, gateway, Options());

        await Assert.ThrowsAsync<BusinessException>(() => service.StartAsync(owner, new() { OrderId = order.Id, Amount = 50m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-unknown" }));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.StartAsync(owner, new() { OrderId = order.Id, Amount = 50m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-unknown" }));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(1, gateway.ChargeCalls);
        Assert.Equal("Unknown", await store.Db.GatewayPaymentIntents.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Camel_case_webhook_matches_intent_and_replay_is_idempotent()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 75m);
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway { ChargeResult = new(true, "tx-camel", null, "provider") };
        var service = new GatewayPaymentService(store.Db, gateway, Options());
        await service.StartAsync(owner, new() { OrderId = order.Id, Amount = 75m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-deposit" });
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { orderId = order.Id, transactionCode = "tx-camel", amount = 75m, currency = "VND", status = "paid" }));

        var accepted = await service.WebhookAsync(raw, "valid", "ts");
        var replay = await service.WebhookAsync(raw, "valid", "ts");

        Assert.True(accepted.Accepted); Assert.False(accepted.Replay);
        Assert.True(replay.Accepted); Assert.True(replay.Replay);
        Assert.Equal("Paid", await store.Db.Orders.Where(x => x.Id == order.Id).Select(x => x.PaymentStatus).SingleAsync());
        Assert.Equal(1, await store.Db.Payments.CountAsync(x => x.PaymentStatus == "Paid"));
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_and_late_webhook_for_canceled_order_requires_reconciliation()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 25m); order.OrderStatus = "Cancelled";
        var intent = new GatewayPaymentIntent { OrderId = order.Id, OwnerId = owner, Amount = 25m, Currency = "VND", TransactionCode = "tx-late", ProviderKey = "p", Status = "Accepted" };
        var payment = new Payment { OrderId = order.Id, Amount = 25m, TransactionCode = "tx-late", PaymentStatus = "Pending" };
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.AddRange(order, intent, payment); await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway(); var service = new GatewayPaymentService(store.Db, gateway, Options());
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { orderId = order.Id, transactionCode = "tx-late", amount = 25m, currency = "VND", status = "paid" }));
        await Assert.ThrowsAsync<BusinessException>(() => service.WebhookAsync(raw, "bad", "ts"));
        var response = await service.WebhookAsync(raw, "valid", "ts");
        var saved = await store.Db.GatewayPaymentIntents.SingleAsync();
        Assert.True(response.Accepted); Assert.Equal("Accepted", saved.Status); Assert.Equal("ReconciliationRequired", saved.ReconciliationStatus);
        Assert.Equal("ReconciliationRequired", await store.Db.Payments.Select(x => x.PaymentStatus).SingleAsync());
    }

    [Fact]
    public async Task Gateway_supports_deposit_then_remaining_balance_without_provider_duplication()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 100m);
        store.Db.Customers.Add(new Customer { Id = owner, Email = owner + "@example.test" }); store.Db.Orders.Add(order); await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway { ChargeResult = new(true, "tx-deposit", null, "") };
        var service = new GatewayPaymentService(store.Db, gateway, Options());
        await service.StartAsync(owner, new() { OrderId = order.Id, Amount = 30m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-deposit" });
        await service.WebhookAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { orderId = order.Id, transactionCode = "tx-deposit", amount = 30m, currency = "VND", status = "paid" })), "valid", "ts");
        gateway.ChargeResult = new(true, "tx-balance", null, "");
        var balance = await service.StartAsync(owner, new() { OrderId = order.Id, Amount = 70m, ReturnUrl = "https://shop.example/return", IdempotencyKey = "intent-balance" });
        Assert.Equal("tx-balance", balance.TransactionCode); Assert.Equal(2, gateway.ChargeCalls);
        Assert.Equal(2, await store.Db.Payments.CountAsync());
    }

    static Order NewOrder(Guid owner, decimal total) => new() { CustomerId = owner, TotalAmount = total, SubTotal = total, OrderCode = "T-" + Guid.NewGuid().ToString("N"), OrderStatus = "Confirmed", PaymentStatus = "Unpaid" };
    static IOptions<CommerceGatewayOptions> Options() => Microsoft.Extensions.Options.Options.Create(new CommerceGatewayOptions { Endpoint = "https://gateway.example", Secret = "secret", MerchantId = "merchant", AllowedReturnOrigins = ["https://shop.example"] });

    sealed class FakeGateway : IPaymentGateway
    {
        public int ChargeCalls; public GatewayChargeResult ChargeResult = new(true, "tx", null, null); public Exception? ChargeException;
        public Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken ct = default) { ChargeCalls++; if (ChargeException is not null) throw ChargeException; return Task.FromResult(ChargeResult); }
        public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken ct = default) => Task.FromResult(new GatewayRefundResult(true, "refund", null));
        public bool VerifyWebhook(ReadOnlySpan<byte> payload, string? signature, string? timestamp) => signature == "valid";
    }
    sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:"); public HoaSapDbContext Db { get; private set; } = null!;
        public static async Task<Store> CreateAsync() { var s = new Store(); await s.Connection.OpenAsync(); s.Db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(s.Connection).Options); await s.Db.Database.EnsureCreatedAsync(); return s; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
