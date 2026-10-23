using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoaSap.BLL;
using HoaSap.BLL.Commerce;
using HoaSap.BLL.CustomOrders;
using HoaSap.DAL;
using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoaSap.Tests;

public sealed class GatewayReconciliationTests
{
    [Fact]
    public async Task Balance_sums_actual_paid_and_pending_rows_uses_custom_deposit_and_blocks_held_intent()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid();
        var order = NewOrder(owner, 100m);
        var customRequest = new CustomOrderRequest { CustomerId = owner, ProductId = Guid.NewGuid(), Quantity = 1, Description = "balance" };
        var quotation = new CustomQuotation { CustomOrderRequestId = customRequest.Id, CurrentRevision = 1 };
        var revision = new CustomQuotationRevision { CustomQuotationId = quotation.Id, Version = 1, UnitPrice = 100m, Quantity = 1, TotalAmount = 100m, DepositAmount = 30m, CompletionDate = DateTime.UtcNow.AddDays(3), ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedBy = owner };
        var checkout = new CustomOrderCheckout { CustomQuotationId = quotation.Id, QuotationVersion = 1, CustomerId = owner, OrderId = order.Id };
        store.Db.AddRange(new Customer { Id = owner, Email = owner + "@example.test" }, order, customRequest, quotation, revision, checkout,
            new Payment { OrderId = order.Id, Amount = 20m, PaymentStatus = "Paid", TransactionCode = "paid-1" },
            new Payment { OrderId = order.Id, Amount = 10m, PaymentStatus = "Pending", TransactionCode = "pending-1" },
            new GatewayPaymentIntent { OrderId = order.Id, OwnerId = owner, Amount = 10m, Status = "Pending" });
        await store.Db.SaveChangesAsync();

        var summary = await new GatewayReconciliationService(store.Db).BalanceAsync(order.Id, owner);

        Assert.Equal(20m, summary.PaidAmount);
        Assert.Equal(10m, summary.PendingAmount);
        Assert.Equal(80m, summary.RemainingAmount);
        Assert.Equal(80m, summary.MinimumNextPayment);
        Assert.False(summary.CanPay);
        await store.Db.Payments.Where(x => x.PaymentStatus == "Paid").ExecuteUpdateAsync(x => x.SetProperty(p => p.PaymentStatus, "Canceled"));
        var depositOnly = await new GatewayReconciliationService(store.Db).BalanceAsync(order.Id, owner);
        Assert.Equal(30m, depositOnly.MinimumNextPayment);
    }

    [Fact]
    public async Task Balance_rejects_cross_owner_access_with_404()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 25m);
        store.Db.AddRange(new Customer { Id = owner, Email = owner + "@example.test" }, order); await store.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => new GatewayReconciliationService(store.Db).BalanceAsync(order.Id, Guid.NewGuid()));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Unknown_intent_marked_not_charged_then_balance_can_retry_without_gateway_call()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 50m);
        var intent = new GatewayPaymentIntent { OrderId = order.Id, OwnerId = owner, Amount = 50m, Status = "Unknown", TransactionCode = "unknown-1" };
        var payment = new Payment { OrderId = order.Id, Amount = 50m, TransactionCode = "unknown-1", PaymentStatus = "Pending" };
        store.Db.AddRange(new Customer { Id = owner, Email = owner + "@example.test" }, order, intent, payment); await store.Db.SaveChangesAsync();
        var service = new GatewayReconciliationService(store.Db);

        var reconciled = await service.ReconcileAsync(intent.Id, new PaymentReconciliationInput { Outcome = "not-charged", EvidenceReference = "provider-case-1" });
        var balance = await service.BalanceAsync(order.Id, owner);

        Assert.Equal("Rejected", reconciled.Status);
        Assert.Equal("ResolvedNotCharged", reconciled.ReconciliationStatus);
        Assert.Equal("Canceled", await store.Db.Payments.Select(x => x.PaymentStatus).SingleAsync());
        Assert.True(balance.CanPay);
    }

    [Fact]
    public async Task Paid_receipt_updates_ledger_and_order_once_without_provider_calls()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var order = NewOrder(owner, 75m);
        var intent = new GatewayPaymentIntent { OrderId = order.Id, OwnerId = owner, Amount = 75m, Status = "Unknown" };
        store.Db.AddRange(new Customer { Id = owner, Email = owner + "@example.test" }, order, intent); await store.Db.SaveChangesAsync();
        var service = new GatewayReconciliationService(store.Db);
        var input = new PaymentReconciliationInput { Outcome = "paid", TransactionCode = "receipt-1", EvidenceReference = "provider-receipt-1" };

        var first = await service.ReconcileAsync(intent.Id, input);
        var replay = await service.ReconcileAsync(intent.Id, input);

        Assert.Equal("Paid", first.Status); Assert.Equal("ResolvedPaid", first.ReconciliationStatus);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(await store.Db.Payments.Where(x => x.PaymentStatus == "Paid").ToListAsync());
        Assert.Equal("Paid", await store.Db.Orders.Select(x => x.PaymentStatus).SingleAsync());
    }

    [Fact]
    public async Task Expired_order_receipt_requires_refund_and_processing_does_not_restore_inventory_or_voucher_twice()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid(); var category = new Category { Code = "R", Name = "R" }; var product = new Product { CategoryId = category.Id, Code = "P", Name = "P", Price = 100m };
        var voucher = new Voucher { Code = "V", DiscountType = "Fixed", DiscountValue = 10m, UsedCount = 1, UsageLimit = 10, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1), IsActive = true };
        var order = NewOrder(owner, 100m); order.OrderStatus = "Expired"; order.VoucherId = voucher.Id;
        var item = new OrderItem { OrderId = order.Id, ProductId = product.Id, Quantity = 2, UnitPrice = 50m, FinalPrice = 100m };
        var inventory = new Inventory { ProductId = product.Id, QuantityAvailable = 3, QuantityReserved = 2 };
        var intent = new GatewayPaymentIntent { OrderId = order.Id, OwnerId = owner, Amount = 100m, Status = "Unknown" };
        store.Db.AddRange(new Customer { Id = owner, Email = owner + "@example.test" }, category, product, voucher, order, item, inventory, intent); await store.Db.SaveChangesAsync();

        var reconcile = await new GatewayReconciliationService(store.Db).ReconcileAsync(intent.Id, new PaymentReconciliationInput { Outcome = "paid", TransactionCode = "expired-receipt", EvidenceReference = "provider-expired-1" });
        Assert.Equal("RefundRequired", reconcile.ReconciliationStatus);
        var refunds = new RefundService(store.Db);
        var requested = await refunds.RequestOrderAsync(order.Id, new RefundRequestDto { Amount = 100m, Reason = "late receipt" });
        var approved = await refunds.ApproveAsync(requested.Id, "admin");
        var gateway = new FakeGateway();
        var processed = await new GatewayRefundService(refunds, gateway, store.Db, ValidOptions()).ProcessAsync(approved.Id, "expired-receipt");

        Assert.Equal("Processed", processed.RefundStatus); Assert.Equal(1, gateway.RefundCalls);
        Assert.Equal(3, await store.Db.Inventories.Select(x => x.QuantityAvailable).SingleAsync());
        Assert.Equal(2, await store.Db.Inventories.Select(x => x.QuantityReserved).SingleAsync());
        Assert.Equal(1, await store.Db.Vouchers.Select(x => x.UsedCount).SingleAsync());
        Assert.Equal("Expired", await store.Db.Orders.Select(x => x.OrderStatus).SingleAsync());
    }

    private static Order NewOrder(Guid owner, decimal total) => new() { CustomerId = owner, TotalAmount = total, SubTotal = total, OrderCode = "T-" + Guid.NewGuid().ToString("N"), OrderStatus = "Confirmed", PaymentStatus = "Unpaid" };
    private static IOptions<CommerceGatewayOptions> ValidOptions() => Options.Create(new CommerceGatewayOptions { Endpoint = "https://gateway.example", Secret = "secret", MerchantId = "merchant", AllowedReturnOrigins = ["https://shop.example"] });

    private sealed class FakeGateway : IPaymentGateway
    {
        public int RefundCalls;
        public Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken ct = default) => Task.FromResult(new GatewayChargeResult(true, "charge", null, null));
        public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken ct = default) { RefundCalls++; return Task.FromResult(new GatewayRefundResult(true, "refund-receipt", null)); }
        public bool VerifyWebhook(ReadOnlySpan<byte> payload, string? signature, string? timestamp) => true;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public HoaSapDbContext Db { get; private set; } = null!;
        public static async Task<Store> CreateAsync() { var s = new Store(); await s.connection.OpenAsync(); s.Db = new HoaSapDbContext(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(s.connection).Options); await s.Db.Database.EnsureCreatedAsync(); return s; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}

[Collection("api")]
public sealed class GatewayReconciliationStaffApiTests : ApiTestBase
{
    private readonly ApiFixture fixture;
    public GatewayReconciliationStaffApiTests(ApiFixture fixture) : base(fixture) => this.fixture = fixture;
    [Fact]
    public async Task Staff_quote_hides_money_and_cannot_quote_reconcile_or_process_financial_endpoints()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new { code = "STAFF-" + Guid.NewGuid().ToString("N")[..8], name = "Staff" })); categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync()); var categoryId = category.RootElement.GetProperty("id").GetGuid();
        var productResponse = await admin.PostAsync("/api/management/products", Json(new { categoryId, code = "STAFF-P-" + Guid.NewGuid().ToString("N")[..8], name = "Staff product", price = 100000m, isActive = true, isCustomizable = true })); productResponse.EnsureSuccessStatusCode();
        using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()); var productId = product.RootElement.GetProperty("id").GetGuid();
        var customerEmail = "staff-owner-" + Guid.NewGuid().ToString("N") + "@example.test";
        var customer = await AuthenticatedAsync(await RegisterAsync(customerEmail));
        var requestResponse = await customer.PostAsync("/api/custom-orders", Json(new { productId, quantity = 1, description = "staff redaction" })); requestResponse.EnsureSuccessStatusCode();
        using var requestJson = JsonDocument.Parse(await requestResponse.Content.ReadAsStringAsync()); var requestId = requestJson.RootElement.GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/custom-orders/{requestId}/quote", Json(new { unitPrice = 200000m, depositPercent = 50m, completionDate = DateTime.UtcNow.AddDays(3), adminNote = "private" }))).EnsureSuccessStatusCode();

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            var account = await db.Customers.SingleAsync(x => x.Email == customerEmail);
            account.Role = "Staff"; await db.SaveChangesAsync();
        }
        var login = await Client.PostAsync("/api/auth/login", Json(new { email = customerEmail, password = "Password-123!" })); login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var staff = await AuthenticatedAsync(loginJson.RootElement.GetProperty("accessToken").GetString()!);

        var view = await staff.GetAsync($"/api/custom-orders/{requestId}"); view.EnsureSuccessStatusCode();
        using var viewJson = JsonDocument.Parse(await view.Content.ReadAsStringAsync());
        var revision = viewJson.RootElement.GetProperty("revisions")[0];
        Assert.Equal(JsonValueKind.Null, revision.GetProperty("unitPrice").ValueKind);
        Assert.Equal(0m, revision.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(0m, viewJson.RootElement.GetProperty("paidAmount").GetDecimal());
        var quoteAttempt = await staff.PostAsync($"/api/custom-orders/{requestId}/quote", Json(new { unitPrice = 1m, depositPercent = 1m, completionDate = DateTime.UtcNow.AddDays(2), adminNote = "no" }));
        var reconcileAttempt = await staff.PostAsync($"/api/gateway/reconciliation/{Guid.NewGuid()}", Json(new { outcome = "not-charged", evidenceReference = "x" }));
        var processAttempt = await staff.PostAsync($"/api/refunds/{Guid.NewGuid()}/process", Json(new { transactionCode = "x" }));
        Assert.Equal(HttpStatusCode.Forbidden, quoteAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reconcileAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, processAttempt.StatusCode);
    }
}
