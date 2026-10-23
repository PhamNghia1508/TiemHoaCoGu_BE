using System.Data;
using System.Security.Cryptography;
using System.Text;
using HoaSap.DAL;
using HoaSap.Domain;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Middleware;

/// <summary>Database-backed fixed-window limiter shared by all application processes.</summary>
public sealed class DistributedRateLimitMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<DistributedRateLimitMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context, HoaSapDbContext db)
    {
        var s = configuration.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new();
        if (!s.Enabled || context.Request.Path.StartsWithSegments("/health")) { await next(context); return; }
        if (!Valid(s)) { await Reject(context, 60); return; }
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromSeconds(s.WindowSeconds);
        var start = new DateTime(now.Ticks - now.Ticks % window.Ticks, DateTimeKind.Utc);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var buckets = new List<(string Key, int Limit)> { (Hash($"api|{ip}|{context.Request.Method}|{RouteBucket(context.Request.Path)}"), s.PermitLimit) };
        if (context.Request.Path.StartsWithSegments("/api/auth")) buckets.Add((Hash($"auth|{ip}"), s.AuthPermitLimit));
        try
        {
            var result = await ConsumeAsync(db, buckets, start, now, s, context.RequestAborted);
            if (!result.Allowed) { await Reject(context, result.RetryAfter); return; }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            log.LogError(ex, "Rate-limit database unavailable; failing closed");
            await Reject(context, s.WindowSeconds);
            return;
        }
        // Never include the downstream pipeline in the database exception handler.
        await next(context);
    }

    private static async Task<(bool Allowed, int RetryAfter)> ConsumeAsync(HoaSapDbContext db, IReadOnlyList<(string Key, int Limit)> buckets, DateTime start, DateTime now, RateLimitSettings s, CancellationToken ct)
    {
        var ordered = buckets.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                db.ChangeTracker.Clear();
                var allowed = true;
                foreach (var bucket in ordered)
                {
                    var updated = await db.Set<RateLimitCounter>().Where(x => x.BucketKey == bucket.Key && x.WindowStartUtc == start)
                        .ExecuteUpdateAsync(u => u.SetProperty(x => x.Count, x => x.Count < bucket.Limit + 1 ? x.Count + 1 : x.Count).SetProperty(x => x.UpdatedAtUtc, now), ct);
                    if (updated == 0)
                    {
                        if (await db.Set<RateLimitCounter>().CountAsync(ct) >= s.MaxBuckets)
                        {
                            await CleanupAsync(db, s, start, ct);
                            if (await db.Set<RateLimitCounter>().CountAsync(ct) >= s.MaxBuckets) { await tx.RollbackAsync(ct); return (false, s.WindowSeconds); }
                        }
                        db.Add(new RateLimitCounter { Id = Guid.NewGuid(), BucketKey = bucket.Key, WindowStartUtc = start, Count = 1, UpdatedAtUtc = now });
                        await db.SaveChangesAsync(ct);
                    }
                    var count = await db.Set<RateLimitCounter>().Where(x => x.BucketKey == bucket.Key && x.WindowStartUtc == start).Select(x => x.Count).SingleAsync(ct);
                    if (count > bucket.Limit) allowed = false;
                }
                await tx.CommitAsync(ct);
                return allowed ? (true, 0) : (false, Math.Max(1, (int)Math.Ceiling((start.AddSeconds(s.WindowSeconds) - now).TotalSeconds)));
            }
            catch (Exception ex) when (attempt < 2 && IsDatabaseFailure(ex))
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception) { /* SQL Server can already have rolled back a deadlock victim. */ }
                db.ChangeTracker.Clear();
            }
        }
        throw new DbUpdateException("Could not atomically consume rate-limit bucket after retry.", (Exception?)null);
    }

    private static async Task CleanupAsync(HoaSapDbContext db, RateLimitSettings s, DateTime currentWindow, CancellationToken ct)
    {
        var stale = await db.Set<RateLimitCounter>().Where(x => x.WindowStartUtc < currentWindow).OrderBy(x => x.WindowStartUtc).Take(Math.Clamp(s.CleanupBatchSize, 1, 10_000)).ToListAsync(ct);
        if (stale.Count == 0) return;
        db.RemoveRange(stale); await db.SaveChangesAsync(ct);
    }
    private static bool Valid(RateLimitSettings s) => s.PermitLimit is >= 1 and <= 1_000_000 && s.AuthPermitLimit is >= 1 and <= 1_000_000 && s.WindowSeconds is >= 1 and <= 86_400 && s.AuthWindowSeconds == s.WindowSeconds && s.MaxBuckets is >= 100 and <= 10_000_000;
    private static bool IsDatabaseFailure(Exception e) => e is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or Microsoft.Data.SqlClient.SqlException;
    private static async Task Reject(HttpContext c, int retry) { if (c.Response.HasStarted) return; c.Response.StatusCode = 429; c.Response.Headers.RetryAfter = retry.ToString(); await c.Response.WriteAsJsonAsync(new { status = 429, title = "Quá nhiều yêu cầu. Vui lòng thử lại sau.", traceId = c.TraceIdentifier }); }
    private static string RouteBucket(PathString p) => p.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "root";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
