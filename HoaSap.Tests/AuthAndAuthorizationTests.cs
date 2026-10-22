using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class AuthAndAuthorizationTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Register_AlwaysCreatesCustomer_AndResponseDoesNotExposePasswordHash()
    {
        var email = $"customer-{Guid.NewGuid():N}@example.test";
        var response = await Client.PostAsync("/api/auth/register", Json(new { fullName = "Normal Customer", phoneNumber = "090" + Random.Shared.Next(1000000, 9999999), email, password = "Password-123!", role = "Admin", isGuest = true }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password-123!", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Customer", json.RootElement.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task LoginAndMe_ReturnSafeIdentityWithoutHash()
    {
        var email = $"me-{Guid.NewGuid():N}@example.test";
        var token = await RegisterAsync(email);
        var client = await AuthenticatedAsync(token);
        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(email, json.RootElement.GetProperty("email").GetString());
        Assert.Equal("Customer", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Anonymous_AdminEndpoint_IsDenied()
    {
        var response = await Client.GetAsync("/api/management/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotUseStaffManualPaymentEndpoint()
    {
        var token = await RegisterAsync($"pay-{Guid.NewGuid():N}@example.test");
        var client = await AuthenticatedAsync(token);
        var response = await client.PostAsync($"/api/payments/order/{Guid.NewGuid()}", Json(new { transactionCode = "TX-QA", paymentMethod = "Cash", amount = 1m }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotCreateWorkshopOrManagementData()
    {
        var token = await RegisterAsync($"staff-{Guid.NewGuid():N}@example.test");
        var client = await AuthenticatedAsync(token);
        var workshop = await client.PostAsync("/api/workshops", Json(new { name = "forbidden", basePrice = 1m, duration = 1, maxCapacity = 1, isActive = true }));
        Assert.Equal(HttpStatusCode.Forbidden, workshop.StatusCode);
        var product = await client.PostAsync("/api/management/products", Json(new { code = "X", name = "forbidden", price = 1m }));
        Assert.Equal(HttpStatusCode.Forbidden, product.StatusCode);
    }

    [Fact]
    public async Task Addresses_AreIsolatedByAuthenticatedOwner()
    {
        var owner = await AuthenticatedAsync(await RegisterAsync($"owner-{Guid.NewGuid():N}@example.test", "091" + Random.Shared.Next(1000000, 9999999)));
        var other = await AuthenticatedAsync(await RegisterAsync($"other-{Guid.NewGuid():N}@example.test", "092" + Random.Shared.Next(1000000, 9999999)));
        var created = await owner.PostAsync("/api/addresses", Json(new { receiverName = "Owner", receiverPhone = "0901234567", detailAddress = "Private", ward = "W", district = "D", province = "P", isDefault = true }));
        created.EnsureSuccessStatusCode();
        var list = await other.GetAsync("/api/addresses");
        list.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Customer_OrderAndBookingCollectionsRequireAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/cart")).StatusCode);
    }
}
