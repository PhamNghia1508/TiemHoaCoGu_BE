using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CmsMediaTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private static MultipartFormDataContent Upload(byte[] bytes, string name, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", name);
        return form;
    }

    [Fact]
    public async Task Upload_requires_admin_and_returns_safe_public_image()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aP1sAAAAASUVORK5CYII=");
        using (var anonymous = Upload(png, "photo.png", "image/png"))
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/api/management/media", anonymous)).StatusCode);
        using var customer = await AuthenticatedAsync(await RegisterAsync("media-" + Guid.NewGuid().ToString("N") + "@test.example"));
        using (var forbidden = Upload(png, "photo.png", "image/png"))
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync("/api/management/media", forbidden)).StatusCode);
        using var admin = await AuthenticatedAsync(await AdminTokenAsync());
        using var form = Upload(png, "../../evil.html", "text/html");
        var result = await admin.PostAsync("/api/management/media", form);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var url = doc.RootElement.GetProperty("url").GetString()!;
        Assert.Matches(@"^/api/media/[a-f0-9]{32}\.png$", url);
        var image = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_rejects_svg_fake_images_empty_and_oversize_files()
    {
        using var admin = await AuthenticatedAsync(await AdminTokenAsync());
        foreach (var bytes in new[] { Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), Encoding.UTF8.GetBytes("not really a png"), Array.Empty<byte>(), new byte[900 * 1024 + 1] })
        {
            using var form = Upload(bytes, "image.png", "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/management/media", form)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/media/not-a-managed-file.svg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/media/" + new string('a', 32) + ".png")).StatusCode);
    }
}
