using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CustomOrderLifecycleTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(HttpClient customer, HttpClient admin, Guid product)> SetupAsync(int stock = 5)
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var category = await admin.PostAsync("/api/management/categories", Json(new { code = "COL-" + Guid.NewGuid().ToString("N")[..8], name = "Custom lifecycle" }));
        category.EnsureSuccessStatusCode();
        using var c = JsonDocument.Parse(await category.Content.ReadAsStringAsync());
        var categoryId = c.RootElement.GetProperty("id").GetGuid();
        var product = await admin.PostAsync("/api/management/products", Json(new { categoryId, code = "COL-P-" + Guid.NewGuid().ToString("N")[..8], name = "Made to order", price = 100000m, isActive = true, isCustomizable = true }));
        product.EnsureSuccessStatusCode();
        using var p = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        var productId = p.RootElement.GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/management/inventory/{productId}?quantity={stock}", Json(new { }))).EnsureSuccessStatusCode();
        var customer = await AuthenticatedAsync(await RegisterAsync("custom-lifecycle-" + Guid.NewGuid().ToString("N") + "@example.test"));
        return (customer, admin, productId);
    }

    private static async Task<(Guid id, int quantity)> ReadRequest(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("id").GetGuid(), json.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Checkout_does_not_reserve_catalog_inventory_and_locks_accepted_revision()
    {
        var (customer, admin, product) = await SetupAsync(7);
        var request = await ReadRequest(await customer.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 2, description = "Custom bouquet" })));
        var first = new { unitPrice = 120000m, depositPercent = 50m, completionDate = DateTime.UtcNow.AddDays(4), adminNote = "v1" };
        (await admin.PostAsync($"/api/custom-orders/{request.id}/quote", Json(first))).EnsureSuccessStatusCode();
        (await customer.PostAsync($"/api/custom-orders/{request.id}/approve", Json(new { version = 1 }))).EnsureSuccessStatusCode();
        var checkout = await customer.PostAsync($"/api/custom-orders/{request.id}/checkout", Json(new { shippingAddress = "12 Hoa Street", idempotencyKey = "custom-" + Guid.NewGuid().ToString("N") }));
        checkout.EnsureSuccessStatusCode();
        var revise = await admin.PostAsync($"/api/custom-orders/{request.id}/quote", Json(new { unitPrice = 999999m, depositPercent = 100m, completionDate = DateTime.UtcNow.AddDays(5), adminNote = "must not change" }));
        Assert.Equal(HttpStatusCode.Conflict, revise.StatusCode);
        var inventory = await admin.GetAsync($"/api/management/products/{product}");
        inventory.EnsureSuccessStatusCode();
        using var inv = JsonDocument.Parse(await inventory.Content.ReadAsStringAsync());
        Assert.Equal(7, inv.RootElement.GetProperty("quantityAvailable").GetInt32());
        Assert.Equal(0, inv.RootElement.GetProperty("quantityReserved").GetInt32());
    }

    [Fact]
    public async Task Deposit_is_required_before_production_and_paid_rows_are_used_for_completion()
    {
        var (customer, admin, product) = await SetupAsync();
        var request = await ReadRequest(await customer.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 1, description = "Deposit workflow" })));
        (await admin.PostAsync($"/api/custom-orders/{request.id}/quote", Json(new { unitPrice = 200000m, depositPercent = 50m, completionDate = DateTime.UtcNow.AddDays(4), adminNote = "deposit" }))).EnsureSuccessStatusCode();
        (await customer.PostAsync($"/api/custom-orders/{request.id}/approve", Json(new { version = 1 }))).EnsureSuccessStatusCode();
        var checkout = await customer.PostAsync($"/api/custom-orders/{request.id}/checkout", Json(new { shippingAddress = "12 Hoa Street", idempotencyKey = "deposit-" + Guid.NewGuid().ToString("N") }));
        checkout.EnsureSuccessStatusCode();
        using var co = JsonDocument.Parse(await checkout.Content.ReadAsStringAsync());
        var orderId = co.RootElement.GetProperty("orderId").GetGuid();
        var startBeforeDeposit = await admin.PostAsync($"/api/custom-orders/{request.id}/production/start", Json(new { }));
        Assert.Equal(HttpStatusCode.Conflict, startBeforeDeposit.StatusCode);
        var staffPayment = await admin.PostAsync($"/api/payments/order/{orderId}", Json(new { transactionCode = "CUSTOM-DEP-" + Guid.NewGuid().ToString("N"), paymentMethod = "Cash", amount = 100000m }));
        staffPayment.EnsureSuccessStatusCode();
        var started = await admin.PostAsync($"/api/custom-orders/{request.id}/production/start", Json(new { }));
        started.EnsureSuccessStatusCode();
        var insufficient = await admin.PostAsync($"/api/custom-orders/{request.id}/complete", Json(new { }));
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_read_or_approve_another_customers_quote_and_staff_money_is_not_exposed()
    {
        var (owner, admin, product) = await SetupAsync();
        var other = await AuthenticatedAsync(await RegisterAsync("custom-other-" + Guid.NewGuid().ToString("N") + "@example.test"));
        var request = await ReadRequest(await owner.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 1, description = "Private quote" })));
        (await admin.PostAsync($"/api/custom-orders/{request.id}/quote", Json(new { unitPrice = 150000m, depositPercent = 50m, completionDate = DateTime.UtcNow.AddDays(3), adminNote = "private" }))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/custom-orders/{request.id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/custom-orders/{request.id}/approve", Json(new { version = 1 }))).StatusCode);
        var staffToken = await RegisterAsync("custom-staffish-" + Guid.NewGuid().ToString("N") + "@example.test");
        var staff = await AuthenticatedAsync(staffToken);
        var staffView = await staff.GetAsync($"/api/custom-orders/{request.id}");
        Assert.Equal(HttpStatusCode.Forbidden, staffView.StatusCode);
    }
}
