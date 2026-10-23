using System.Net;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CmsWebTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Admin_shell_and_OpenApi_are_available_without_exposing_private_data()
    {
        var page = await Client.GetAsync("/admin/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("script-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single());
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("admin.js", html);
        Assert.DoesNotContain(ApiFixture.AdminPassword, html);
        var script = await Client.GetAsync("/admin/admin.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.DoesNotContain("localStorage", await script.Content.ReadAsStringAsync());
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/management/accounts", out _));
        var upload = paths.GetProperty("/api/management/media").GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        Assert.True(paths.TryGetProperty("/api/management/orders/{id}", out _));
    }
}
