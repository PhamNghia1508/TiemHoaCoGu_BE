using System.Net;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class StorefrontWebTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Storefront_shell_and_assets_are_public_and_external_scripted()
    {
        var page = await Client.GetAsync("/shop/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("shop.css", html);
        Assert.Contains("shop.js", html);
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        var script = await Client.GetAsync("/shop/shop.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        var js = await script.Content.ReadAsStringAsync();
        Assert.DoesNotContain("localStorage", js, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("textContent", js);
    }
}
