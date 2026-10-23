using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class RequestDiagnosticsTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task ServerRequestIdIsPresentAndDoesNotTrustClientHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Request-ID", "untrusted-client-value");
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = Assert.Single(response.Headers.GetValues("X-Request-ID"));
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.NotEqual("untrusted-client-value", id);
    }

    [Fact]
    public async Task BusinessProblemHasMatchingRequestIdAndProblemContentType()
    {
        using var admin = await AuthenticatedAsync(await AdminTokenAsync());
        using var response = await admin.PostAsync($"/api/payments/order/{Guid.NewGuid()}", Json(new
        {
            amount = 10000, paymentMethod = "Cash", transactionCode = "missing-" + Guid.NewGuid().ToString("N")
        }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Assert.Single(response.Headers.GetValues("X-Request-ID")), body.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("stackTrace", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticationRateLimitHasRetryAfterAndTraceId()
    {
        // Isolated limiter state: this test must not throttle the shared API fixture.
        await using var isolated = new ApiFixture(15);
        using var client = isolated.CreateClient();
        HttpResponseMessage? limited = null;
        try
        {
            for (var i = 0; i < 16; i++)
            {
                var response = await client.PostAsync("/api/auth/login", Json(new { email = "missing@example.test", password = "Invalid-password!" }));
                if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = response; break; }
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                response.Dispose();
            }
            Assert.NotNull(limited);
            Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
            Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(Assert.Single(limited.Headers.GetValues("X-Request-ID")), body.RootElement.GetProperty("traceId").GetString());
        }
        finally { limited?.Dispose(); }
    }
}
