using System.Net;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class WorkshopAdminWebTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Admin_shell_exposes_workshop_management_without_persisted_tokens()
    {
        var page = await Client.GetAsync("/admin/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-tab=\"workshops\"", html);
        Assert.Contains("admin.js", html);
        var script = await Client.GetAsync("/admin/admin.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        var js = await script.Content.ReadAsStringAsync();
        Assert.Contains("/api/workshops", js);
        Assert.Contains("roster", js);
        Assert.DoesNotContain("localStorage", js, StringComparison.OrdinalIgnoreCase);
    }
}
