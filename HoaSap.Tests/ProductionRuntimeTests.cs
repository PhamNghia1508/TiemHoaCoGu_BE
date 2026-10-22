using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class ProductionRuntimeTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task ReadinessAcceptsInitializedSchema()
    {
        var response = await Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NullCheckoutLinesReturnClientErrorNotServerError()
    {
        var response = await Client.PostAsync("/api/orders/checkout", Json(new
        {
            shippingAddress = "QA address", receiverName = "QA", receiverPhone = "0901234567",
            items = new object?[] { null }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NullCheckoutArrayIsRejected()
    {
        var response = await Client.PostAsync("/api/orders/checkout", Json(new
        {
            shippingAddress = "QA address", receiverName = "QA", receiverPhone = "0901234567",
            items = (object?)null
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProductRejectsSubCentPriceBeforeDatabaseRounding()
    {
        using var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new
        {
            code = "SCALE-" + Guid.NewGuid().ToString("N")[..8], name = "Scale test"
        }));
        categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var response = await admin.PostAsync("/api/management/products", Json(new
        {
            categoryId = category.RootElement.GetProperty("id").GetGuid(),
            code = "SCALE-" + Guid.NewGuid().ToString("N")[..8], name = "Flower",
            price = 123.001m, isActive = true
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
