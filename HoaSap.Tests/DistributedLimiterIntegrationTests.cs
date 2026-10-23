using System.Net;
using HoaSap.API.Middleware;
using HoaSap.DAL;
using HoaSap.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoaSap.Tests;

public sealed class DistributedLimiterIntegrationTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"rate-integration-{Guid.NewGuid():N}.db");
    private HoaSapDbContext Database() => new(new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite($"Data Source={path};Default Timeout=5").Options);
    private static IConfiguration Config(int permit = 3, int max = 100) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RateLimiting:PermitLimit"] = permit.ToString(), ["RateLimiting:AuthPermitLimit"] = "2", ["RateLimiting:MaxBuckets"] = max.ToString()
    }).Build();
    private static DefaultHttpContext Context(string path = "/api/catalog/products")
    {
        var context = new DefaultHttpContext(); context.Request.Path = path; context.Request.Method = "GET";
        context.Connection.RemoteIpAddress = IPAddress.Loopback; context.Response.Body = new MemoryStream(); return context;
    }
    [Fact]
    public async Task TwoInstancesShareCounters_AndReturnRetryAfter()
    {
        await using (var setup = Database()) await setup.Database.EnsureCreatedAsync();
        var reached = 0;
        var one = new DistributedRateLimitMiddleware(_ => { reached++; return Task.CompletedTask; }, Config(), NullLogger<DistributedRateLimitMiddleware>.Instance);
        var two = new DistributedRateLimitMiddleware(_ => { reached++; return Task.CompletedTask; }, Config(), NullLogger<DistributedRateLimitMiddleware>.Instance);
        for (var i = 0; i < 5; i++)
        {
            await using var db = Database(); var ctx = Context(); await (i % 2 == 0 ? one : two).InvokeAsync(ctx, db);
            Assert.Equal(i < 3 ? 200 : 429, ctx.Response.StatusCode);
            if (i >= 3) Assert.True(int.Parse(ctx.Response.Headers.RetryAfter.ToString()) >= 1);
        }
        Assert.Equal(3, reached);
        // Rejected requests saturate the counter at quota + 1 to avoid overflow under abuse.
        await using var final = Database(); Assert.Equal(4, (await final.Set<RateLimitCounter>().SingleAsync()).Count);
    }
    [Fact]
    public async Task AuthHasStricterSharedLimitAcrossAuthPaths()
    {
        await using (var setup = Database()) await setup.Database.EnsureCreatedAsync();
        var limiter = new DistributedRateLimitMiddleware(_ => Task.CompletedTask, Config(50), NullLogger<DistributedRateLimitMiddleware>.Instance);
        foreach (var route in new[] { "login", "register", "forgot-password" })
        {
            await using var db = Database(); var ctx = Context("/api/auth/" + route); await limiter.InvokeAsync(ctx, db);
            Assert.Equal(route == "forgot-password" ? 429 : 200, ctx.Response.StatusCode);
        }
    }
    [Fact]
    public async Task FullCardinalityRecoversByCleaningStaleWindows()
    {
        await using (var setup = Database())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.AddRange(Enumerable.Range(0, 100).Select(i => new RateLimitCounter { BucketKey = i.ToString().PadLeft(64, '0'), WindowStartUtc = DateTime.UtcNow.AddHours(-2), UpdatedAtUtc = DateTime.UtcNow.AddHours(-2) }));
            await setup.SaveChangesAsync();
        }
        var limiter = new DistributedRateLimitMiddleware(_ => Task.CompletedTask, Config(), NullLogger<DistributedRateLimitMiddleware>.Instance);
        await using var db = Database(); var ctx = Context(); await limiter.InvokeAsync(ctx, db); Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal(1, await db.Set<RateLimitCounter>().CountAsync());
    }
    [Fact]
    public async Task DownstreamDatabaseErrorsAreNotSwallowedAsRateLimitFailures()
    {
        await using (var setup = Database()) await setup.Database.EnsureCreatedAsync();
        var limiter = new DistributedRateLimitMiddleware(_ => throw new InvalidOperationException("downstream"), Config(), NullLogger<DistributedRateLimitMiddleware>.Instance);
        await using var db = Database(); var ctx = Context();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => limiter.InvokeAsync(ctx, db));
        Assert.Equal("downstream", error.Message); Assert.NotEqual(429, ctx.Response.StatusCode);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) try { File.Delete(path + suffix); } catch { } }
}
