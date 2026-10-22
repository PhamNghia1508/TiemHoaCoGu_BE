using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoaSap.API.Security;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class AuditApiTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task AuditLogs_AreAdminOnly()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/management/audit-logs")).StatusCode);

        var customer = await AuthenticatedAsync(await RegisterAsync($"audit-customer-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/management/audit-logs")).StatusCode);

        var staffEmail = $"audit-staff-{Guid.NewGuid():N}@example.test";
        const string staffPassword = "Staff-Password-123!";
        Guid staffId;
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            var staff = new Customer
            {
                FullName = "QA Staff",
                Email = staffEmail,
                PhoneNumber = "09" + Random.Shared.Next(100000000, 999999999),
                PasswordHash = PasswordService.Hash(staffPassword),
                Role = "Staff",
                IsGuest = false
            };
            db.Customers.Add(staff);
            await db.SaveChangesAsync();
            staffId = staff.Id;
        }

        var login = await Client.PostAsync("/api/auth/login", Json(new { email = staffEmail, password = staffPassword }));
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var staffClient = await AuthenticatedAsync(loginJson.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/management/audit-logs")).StatusCode);
        Assert.NotEqual(Guid.Empty, staffId);
    }

    [Fact]
    public async Task AdminCanFilterAuditLogsAndPagingBoundsNeverProduceServerError()
    {
        var adminToken = await AdminTokenAsync();
        var admin = await AuthenticatedAsync(adminToken);
        using var me = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        var adminId = me.RootElement.GetProperty("id").GetGuid();

        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new { code = $"AUD-{Guid.NewGuid():N}"[..12], name = "Audit category" }));
        categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = category.RootElement.GetProperty("id").GetGuid();

        var productResponse = await admin.PostAsync("/api/management/products", Json(new
        {
            categoryId,
            code = $"AUD-P-{Guid.NewGuid():N}"[..12],
            name = "Audit product",
            price = 12.34m,
            isActive = true
        }));
        productResponse.EnsureSuccessStatusCode();
        using var product = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync());
        var productId = product.RootElement.GetProperty("id").GetGuid();

        var from = DateTime.UtcNow.AddMinutes(-2).ToString("O");
        var to = DateTime.UtcNow.AddMinutes(2).ToString("O");
        var filtered = await admin.GetAsync($"/api/management/audit-logs?entityType=Product&entityId={productId}&actorId={adminId}&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&page=1&pageSize=10");
        filtered.EnsureSuccessStatusCode();
        using var filteredJson = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        var items = filteredJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("entityType").GetString() == "Product" && item.GetProperty("entityId").GetString() == productId.ToString() && item.GetProperty("actorId").GetGuid() == adminId);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/management/audit-logs?pageSize=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/management/audit-logs?pageSize=101")).StatusCode);
        var hugePage = await admin.GetAsync("/api/management/audit-logs?page=21474836&pageSize=100");
        Assert.NotEqual(HttpStatusCode.InternalServerError, hugePage.StatusCode);
        Assert.True(hugePage.IsSuccessStatusCode || hugePage.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ManagementProductAuditHasActorAndResponseTraceButNoSensitivePayload()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var categoryResponse = await admin.PostAsync("/api/management/categories", Json(new { code = $"SEC-{Guid.NewGuid():N}"[..12], name = "Secure category" }));
        categoryResponse.EnsureSuccessStatusCode();
        using var category = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = category.RootElement.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/management/products")
        {
            Content = Json(new
            {
                categoryId,
                code = $"SEC-P-{Guid.NewGuid():N}"[..12],
                name = "Secure product",
                price = 9.99m,
                description = "SENSITIVE-DESCRIPTION-PAYLOAD",
                thumbnailUrl = "https://example.test/sensitive-image-token",
                badge = "New",
                isActive = true
            })
        };
        request.Headers.Add("X-Request-ID", "client-supplied-request-id");
        var created = await admin.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var responseTrace = created.Headers.GetValues("X-Request-ID").Single();
        using var product = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var productId = product.RootElement.GetProperty("id").GetGuid();

        var logs = await admin.GetAsync($"/api/management/audit-logs?entityType=Product&entityId={productId}&pageSize=10");
        logs.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await logs.Content.ReadAsStringAsync());
        var log = body.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("entityId").GetString() == productId.ToString());
        Assert.Equal("Product", log.GetProperty("entityType").GetString());
        Assert.Equal(await AdminIdAsync(admin), log.GetProperty("actorId").GetGuid());
        Assert.Equal("Admin", log.GetProperty("actorRole").GetString());
        Assert.Equal(responseTrace, log.GetProperty("traceId").GetString());
        var serialized = log.GetProperty("newValuesJson").GetString()!;
        Assert.DoesNotContain("SENSITIVE-DESCRIPTION-PAYLOAD", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-image-token", serialized, StringComparison.Ordinal);
    }

    private static async Task<Guid> AdminIdAsync(HttpClient admin)
    {
        using var json = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
