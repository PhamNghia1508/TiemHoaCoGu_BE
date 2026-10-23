using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace HoaSap.API.Middleware;

/// <summary>Server-generated request IDs; never trust a caller-provided ID for log correlation.</summary>
public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId });
        var started = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            // Log the route template, not raw URLs, query strings, tokens, or request bodies.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            logger.LogInformation("HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs:F2} ms; aborted={Aborted}",
                context.Request.Method, route, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.RequestAborted.IsCancellationRequested);
        }
    }
}
