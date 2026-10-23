using System.Net;
using HoaSap.API.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

public sealed class TrustedProxyTests
{
    private static Dictionary<string, string?> Settings(bool trust = true) => new()
    {
        ["RateLimiting:TrustForwardedHeaders"] = trust.ToString(),
        ["RateLimiting:TrustedProxies:0"] = "10.0.0.10",
        ["RateLimiting:Distributed"] = "false"
    };

    private static WebApplicationFactory<Program> Factory(ApiFixture fixture, Dictionary<string, string?> settings) =>
        fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

    private static Task<HttpContext> Send(WebApplicationFactory<Program> factory, string peer = "10.0.0.10", string forwarded = "198.51.100.1", string path = "/health") =>
        factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("flowers.example");
            context.Request.Path = path;
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

    [Fact]
    public async Task TrustedProxy_NormalizesIpAndScheme_UsingClosestHopOnly()
    {
        await using var fixture = new ApiFixture();
        await using var factory = Factory(fixture, Settings());
        var context = await Send(factory, forwarded: "203.0.113.99, 198.51.100.1");
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("198.51.100.1", context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("https", context.Request.Scheme);
    }

    [Theory]
    [InlineData(true, "198.51.100.20")]
    [InlineData(true, "127.0.0.1")]
    [InlineData(false, "10.0.0.10")]
    public async Task UntrustedOrDisabledForwarding_IgnoresClientHeaders(bool trust, string peer)
    {
        await using var fixture = new ApiFixture();
        await using var factory = Factory(fixture, Settings(trust));
        var context = await Send(factory, peer);
        Assert.Equal(peer, context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task IPv4MappedProxy_IsRecognizedOnDualModeSockets()
    {
        await using var fixture = new ApiFixture();
        await using var factory = Factory(fixture, Settings());
        var context = await Send(factory, "::ffff:10.0.0.10");
        Assert.Equal("198.51.100.1", context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task InvalidForwardedIp_DoesNotReplacePeerOrScheme()
    {
        await using var fixture = new ApiFixture();
        await using var factory = Factory(fixture, Settings());
        var context = await Send(factory, forwarded: "not-an-ip");
        Assert.Equal("10.0.0.10", context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public void ForwardingFailsClosed_WhenProxyListIsEmptyOrInvalid()
    {
        foreach (var proxy in new[] { null, "not-an-ip", "10.0.0.0/24" })
        {
            var settings = Settings();
            settings["RateLimiting:TrustedProxies:0"] = proxy;
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            Assert.Throws<InvalidOperationException>(() => TrustedProxyConfiguration.CreateOptions(config));
        }
    }

    [Fact]
    public async Task ProductionHttpsRedirect_UsesTrustedScheme_ButNotSpoofedScheme()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build();
        using var server = new TestServer(new WebHostBuilder().UseEnvironment("Production")
            .ConfigureServices(services => services.AddHttpsRedirection(options => options.HttpsPort = 443))
            .Configure(app =>
            {
                app.UseForwardedHeaders(TrustedProxyConfiguration.CreateOptions(configuration));
                app.UseHsts();
                app.UseHttpsRedirection();
                app.Run(context => context.Response.WriteAsync("ok"));
            }));
        Task<HttpContext> Request(string peer) => server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("flowers.example");
            context.Request.Path = "/health";
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        var trusted = await Request("10.0.0.10");
        Assert.Equal(200, trusted.Response.StatusCode);
        var untrusted = await Request("198.51.100.20");
        Assert.Equal(307, untrusted.Response.StatusCode);
        Assert.Equal("https://flowers.example/health", untrusted.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task MemoryLimiter_PartitionsTrustedClients_AndIgnoresSpoofedLeftmostIp()
    {
        await using var fixture = new ApiFixture();
        var settings = Settings();
        settings["RateLimiting:PermitLimit"] = "1";
        await using var factory = Factory(fixture, settings);
        Assert.Equal(200, (await Send(factory, forwarded: "203.0.113.1, 198.51.100.1")).Response.StatusCode);
        Assert.Equal(200, (await Send(factory, forwarded: "203.0.113.1, 198.51.100.2")).Response.StatusCode);
        Assert.Equal(429, (await Send(factory, forwarded: "203.0.113.2, 198.51.100.1")).Response.StatusCode);
    }

    [Fact]
    public async Task DistributedLimiter_UsesSameTrustedClientBoundary()
    {
        await using var fixture = new ApiFixture();
        var settings = Settings();
        settings["RateLimiting:Distributed"] = "true";
        // Use a generous memory quota and the distributed auth quota to distinguish them.
        // The route is intentionally unmapped: the limiter still runs, without auth policy metadata.
        settings["RateLimiting:AuthPermitLimit"] = "2";
        await using var factory = Factory(fixture, settings);
        const string path = "/api/auth/proxy-regression";
        Assert.Equal(404, (await Send(factory, forwarded: "203.0.113.1, 198.51.100.1", path: path)).Response.StatusCode);
        Assert.Equal(404, (await Send(factory, forwarded: "203.0.113.2, 198.51.100.1", path: path)).Response.StatusCode);
        Assert.Equal(429, (await Send(factory, forwarded: "203.0.113.3, 198.51.100.1", path: path)).Response.StatusCode);
        Assert.Equal(404, (await Send(factory, forwarded: "203.0.113.3, 198.51.100.2", path: path)).Response.StatusCode);
    }
}
