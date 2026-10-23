using System.Net;
using System.Text.Json;
using Xunit;
namespace HoaSap.Tests;

[Collection("api")]
public sealed class CustomOrderWorkflowTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    async Task<(HttpClient customer, HttpClient admin, Guid product)> SetupAsync()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var category = await admin.PostAsync("/api/management/categories", Json(new { code = "CUS-" + Guid.NewGuid().ToString("N")[..8], name = "Hoa tùy biến" }));
        category.EnsureSuccessStatusCode();
        using var c = JsonDocument.Parse(await category.Content.ReadAsStringAsync());
        var product = await admin.PostAsync("/api/management/products", Json(new { categoryId = c.RootElement.GetProperty("id").GetGuid(), code = "CUS-P-" + Guid.NewGuid().ToString("N")[..8], name = "Sản phẩm đặt trước", price = 100000m, isActive = true, isCustomizable = true, isPreOrder = true }));
        product.EnsureSuccessStatusCode();
        using var p = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        var customer = await AuthenticatedAsync(await RegisterAsync("custom-" + Guid.NewGuid().ToString("N") + "@example.test"));
        return (customer, admin, p.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CustomerCannotCreateOrReviseQuoteAsStaff()
    {
        var (customer, _, product) = await SetupAsync();
        var request = await customer.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 1, description = "Mẫu hoa theo yêu cầu" }));
        request.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        var forbidden = await customer.PostAsync($"/api/custom-orders/{id}/quote", Json(new { unitPrice = 100000m, depositPercent = 50, completionDate = DateTime.UtcNow.AddDays(3), adminNote = "Không được" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ApprovingStaleQuoteVersionIsRejected()
    {
        var (customer, admin, product) = await SetupAsync();
        var request = await customer.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 2, description = "Mẫu hoa cưới" })); request.EnsureSuccessStatusCode();
        using var rb = JsonDocument.Parse(await request.Content.ReadAsStringAsync()); var id = rb.RootElement.GetProperty("id").GetGuid();
        var quote = new { unitPrice = 120000m, depositPercent = 50, completionDate = DateTime.UtcNow.AddDays(3), adminNote = "Báo giá" };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/custom-orders/{id}/quote", Json(quote))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/custom-orders/{id}/quote", Json(new { unitPrice = 150000m, depositPercent = 50, completionDate = DateTime.UtcNow.AddDays(4), adminNote = "Báo giá cập nhật" }))).StatusCode);
        var stale = await customer.PostAsync($"/api/custom-orders/{id}/approve", Json(new { version = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task CheckoutRetryWithSameKeyReturnsSameOrder()
    {
        var (customer, admin, product) = await SetupAsync();
        var request = await customer.PostAsync("/api/custom-orders", Json(new { productId = product, quantity = 1, description = "Bó hoa preorder" })); request.EnsureSuccessStatusCode();
        using var rb = JsonDocument.Parse(await request.Content.ReadAsStringAsync()); var id = rb.RootElement.GetProperty("id").GetGuid();
        var quoted = await admin.PostAsync($"/api/custom-orders/{id}/quote", Json(new { unitPrice = 180000m, depositPercent = 50, completionDate = DateTime.UtcNow.AddDays(4), adminNote = "Báo giá" }));
        Assert.True(quoted.IsSuccessStatusCode, await quoted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await customer.PostAsync($"/api/custom-orders/{id}/approve", Json(new { version = 1 }))).StatusCode);
        var key = "retry-" + Guid.NewGuid().ToString("N");
        var first = await customer.PostAsync($"/api/custom-orders/{id}/checkout", Json(new { shippingAddress = "12 Đường Hoa, TP.HCM", idempotencyKey = key })); first.EnsureSuccessStatusCode();
        using var f = JsonDocument.Parse(await first.Content.ReadAsStringAsync()); var orderId = f.RootElement.GetProperty("orderId").GetGuid();
        var second = await customer.PostAsync($"/api/custom-orders/{id}/checkout", Json(new { shippingAddress = "12 Đường Hoa, TP.HCM", idempotencyKey = key }));
        second.EnsureSuccessStatusCode();
        using var s = JsonDocument.Parse(await second.Content.ReadAsStringAsync()); Assert.Equal(orderId, s.RootElement.GetProperty("orderId").GetGuid());
    }
}
