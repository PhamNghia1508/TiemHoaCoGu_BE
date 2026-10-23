using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CommerceTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Checkout_UsesServerProductPriceAndServerShipping()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new { code = "QA-CAT-" + Guid.NewGuid().ToString("N")[..8], name = "QA Flowers" }));
        categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = category.RootElement.GetProperty("id").GetGuid();
        var productResponse = await admin.PostAsync("/api/management/products", Json(new { code = "QA-" + Guid.NewGuid().ToString("N")[..8], name = "QA Rose", categoryId, price = 125000m, isActive = true }));
        productResponse.EnsureSuccessStatusCode();
        using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync());
        var productId = product.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/management/inventory/{productId}?quantity=3", Json(new { }))).StatusCode);

        var customer = await AuthenticatedAsync(await RegisterAsync($"checkout-{Guid.NewGuid():N}@example.test", "093" + Random.Shared.Next(1000000, 9999999)));
        var order = await customer.PostAsync("/api/orders/checkout", Json(new { shippingAddress = "QA address", items = new[] { new { productId, quantity = 1, unitPrice = 1m } } }));
        order.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        Assert.Equal(125000m, body.RootElement.GetProperty("subTotal").GetDecimal());
        Assert.Equal(30000m, body.RootElement.GetProperty("shippingFee").GetDecimal());
        Assert.Equal(155000m, body.RootElement.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(125000m, body.RootElement.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task Checkout_InsufficientStockDoesNotReserveInventory()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new { code = "QA-CAT-" + Guid.NewGuid().ToString("N")[..8], name = "QA Flowers" }));
        categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = category.RootElement.GetProperty("id").GetGuid();
        var productResponse = await admin.PostAsync("/api/management/products", Json(new { code = "QA-" + Guid.NewGuid().ToString("N")[..8], name = "QA Tulip", categoryId, price = 50000m, isActive = true }));
        productResponse.EnsureSuccessStatusCode();
        using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync());
        var productId = product.RootElement.GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/management/inventory/{productId}?quantity=1", Json(new { }))).EnsureSuccessStatusCode();
        var customer = await AuthenticatedAsync(await RegisterAsync($"stock-{Guid.NewGuid():N}@example.test", "094" + Random.Shared.Next(1000000, 9999999)));
        var line = new[] { new { productId, quantity = 2 } };
        var rejected = await customer.PostAsync("/api/orders/checkout", Json(new { shippingAddress = "QA", items = line }));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var succeedsAfterFailedAttempt = await customer.PostAsync("/api/orders/checkout", Json(new { shippingAddress = "QA", items = new[] { new { productId, quantity = 1 } } }));
        Assert.NotEqual(HttpStatusCode.Conflict, succeedsAfterFailedAttempt.StatusCode);
        succeedsAfterFailedAttempt.EnsureSuccessStatusCode();
    }
}
