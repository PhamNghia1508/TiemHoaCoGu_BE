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

public sealed class GatewayRefundTests
{
    [Fact]
    public async Task Missing_configuration_leaves_approved_refund_without_attempt()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "gw-1", "GenericSignedGateway");
        var gateway = new FakeGateway();
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, Options.Create(new CommerceGatewayOptions()));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.ProcessAsync(data.refund.Id, "gw-1"));
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(0, gateway.RefundCalls);
        Assert.Equal("Approved", await store.Db.Refunds.Where(x => x.Id == data.refund.Id).Select(x => x.RefundStatus).SingleAsync());
        Assert.Empty(await store.Db.Set<GatewayRefundAttempt>().ToListAsync());
    }

    [Fact]
    public async Task Invalid_original_source_is_rejected_without_provider_call()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "cash-1", "Cash");
        var gateway = new FakeGateway();
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, ValidOptions());
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.ProcessAsync(data.refund.Id, "cash-1"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, gateway.RefundCalls);
        Assert.Equal("Approved", await store.Db.Refunds.Where(x => x.Id == data.refund.Id).Select(x => x.RefundStatus).SingleAsync());
    }

    [Fact]
    public async Task Valid_gateway_refund_calls_once_and_is_replay_safe()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "gw-valid", "GenericSignedGateway");
        var gateway = new FakeGateway { RefundResult = new(true, "provider-ref-1", null) };
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, ValidOptions());
        var first = await service.ProcessAsync(data.refund.Id, "gw-valid");
        var replay = await service.ProcessAsync(data.refund.Id, "gw-valid");
        Assert.Equal("Processed", first.RefundStatus);
        Assert.Equal("Processed", replay.RefundStatus);
        Assert.Equal(1, gateway.RefundCalls);
        var attempt = await store.Db.Set<GatewayRefundAttempt>().SingleAsync();
        Assert.Equal("Completed", attempt.Status);
        Assert.Equal("provider-ref-1", attempt.ProviderTransactionCode);
    }

    [Fact]
    public async Task Timeout_persists_unknown_processing_state_and_never_repeats()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "gw-timeout", "GenericSignedGateway");
        var gateway = new FakeGateway { RefundException = new TaskCanceledException("provider timeout") };
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, ValidOptions());
        var first = await Assert.ThrowsAsync<BusinessException>(() => service.ProcessAsync(data.refund.Id, "gw-timeout"));
        Assert.Equal(503, first.StatusCode);
        Assert.Equal("Processing", await store.Db.Refunds.Where(x => x.Id == data.refund.Id).Select(x => x.RefundStatus).SingleAsync());
        Assert.Equal("Unknown", await store.Db.Set<GatewayRefundAttempt>().Select(x => x.Status).SingleAsync());
        var second = await Assert.ThrowsAsync<BusinessException>(() => service.ProcessAsync(data.refund.Id, "gw-timeout"));
        Assert.Equal(409, second.StatusCode);
        Assert.Equal(1, gateway.RefundCalls);
    }

    [Fact]
    public async Task Per_source_limit_is_enforced_while_multiple_gateway_payments_remain_independent()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "gw-a", "GenericSignedGateway");
        data.refund.RefundAmount = 101m;
        await store.Db.SaveChangesAsync();
        store.Db.Payments.Add(new Payment { OrderId = data.order.Id, TransactionCode = "gw-b", PaymentMethod = "GenericSignedGateway", PaymentType = "Gateway", Amount = 100m, PaymentStatus = "Paid", PaidAt = DateTime.UtcNow });
        await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, ValidOptions());
        var tooMuch = await Assert.ThrowsAsync<BusinessException>(() => service.ProcessAsync(data.refund.Id, "gw-a"));
        Assert.Equal(400, tooMuch.StatusCode);
        Assert.Equal(0, gateway.RefundCalls);
        var secondRefund = new Refund { OrderId = data.order.Id, RefundAmount = 100m, RefundReason = "second source", RefundStatus = "Approved", ApprovedBy = "admin" };
        store.Db.Refunds.Add(secondRefund); await store.Db.SaveChangesAsync();
        var result = await service.ProcessAsync(secondRefund.Id, "gw-b");
        Assert.Equal("Processed", result.RefundStatus);
        Assert.Equal(1, gateway.RefundCalls);
    }

    [Fact]
    public async Task Reconcile_known_receipt_completes_ledger_without_invoking_gateway()
    {
        await using var store = await Store.CreateAsync();
        var data = await store.SeedAsync(100m, "gw-reconcile", "GenericSignedGateway");
        var attempt = new GatewayRefundAttempt { RefundId = data.refund.Id, PaymentId = data.payment.Id, Amount = data.refund.RefundAmount, Status = "Unknown" };
        store.Db.Set<GatewayRefundAttempt>().Add(attempt);
        data.refund.RefundStatus = "Processing";
        await store.Db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new GatewayRefundService(new RefundService(store.Db), gateway, store.Db, ValidOptions());
        var result = await service.ReconcileAsync(data.refund.Id, "known-receipt");
        Assert.Equal("Processed", result.RefundStatus);
        Assert.Equal(0, gateway.RefundCalls);
        Assert.Equal("Completed", (await store.Db.Set<GatewayRefundAttempt>().SingleAsync()).Status);
    }

    private static IOptions<CommerceGatewayOptions> ValidOptions() => Options.Create(new CommerceGatewayOptions { Endpoint = "https://gateway.example", Secret = "secret", MerchantId = "merchant", AllowedReturnOrigins = ["https://shop.example"] });

    private sealed class FakeGateway : IPaymentGateway
    {
        public int RefundCalls;
        public GatewayRefundResult RefundResult = new(true, "provider-ref", null);
        public Exception? RefundException;
        public Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken ct = default) => Task.FromResult(new GatewayChargeResult(true, "charge", null, null));
        public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken ct = default)
        { RefundCalls++; if (RefundException is not null) throw RefundException; return Task.FromResult(RefundResult); }
        public bool VerifyWebhook(ReadOnlySpan<byte> payload, string? signature, string? timestamp) => true;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public HoaSapDbContext Db { get; private set; } = null!;
        public static async Task<Store> CreateAsync()
        { var store = new Store(); await store.connection.OpenAsync(); store.Db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(store.connection).Options); await store.Db.Database.EnsureCreatedAsync(); return store; }
        public async Task<(Order order, Payment payment, Refund refund)> SeedAsync(decimal amount, string code, string method)
        { var customer = new Customer { Email = Guid.NewGuid() + "@example.test", PhoneNumber = "09" + Random.Shared.Next(100000000, 999999999) }; var order = new Order { CustomerId = customer.Id, OrderCode = "R-" + Guid.NewGuid().ToString("N"), TotalAmount = amount, SubTotal = amount, OrderStatus = "Confirmed", PaymentStatus = "Paid" }; var payment = new Payment { OrderId = order.Id, TransactionCode = code, PaymentMethod = method, PaymentType = "Gateway", Amount = amount, PaymentStatus = "Paid", PaidAt = DateTime.UtcNow }; var refund = new Refund { OrderId = order.Id, RefundAmount = amount, RefundReason = "gateway test", RefundStatus = "Approved", ApprovedBy = "admin" }; Db.Customers.Add(customer); Db.Orders.Add(order); Db.Payments.Add(payment); Db.Refunds.Add(refund); await Db.SaveChangesAsync(); return (order, payment, refund); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
