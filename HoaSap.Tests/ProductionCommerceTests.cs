using System.Net;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class ProductionCommerceTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task CheckoutRejectsInvalidRawLineBeforeGrouping()
    {
        var customer = await AuthenticatedAsync(await RegisterAsync($"invalid-{Guid.NewGuid():N}@example.test"));
        var response = await customer.PostAsync("/api/orders/checkout", Json(new { shippingAddress = "QA", items = new[] { new { productId = Guid.NewGuid(), quantity = 0 }, new { productId = Guid.NewGuid(), quantity = 1 } } }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrdersListRequiresAuthentication()
    {
        var response = await Client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
