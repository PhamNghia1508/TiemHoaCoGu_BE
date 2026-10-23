using System.Text.Json;
using HoaSap.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoaSap.Tests;

public sealed class DatabaseConflictResponseTests
{
    [Fact]
    public async Task EfWrappedDatabaseConflictReturns409WithoutDatabaseDetails()
    {
        var error = new InvalidOperationException("SQL execution strategy wrapper",
            new DbUpdateException("sensitive database diagnostic"));
        var context = await InvokeAsync(error);
        Assert.Equal(409, context.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("sensitive", body.RootElement.ToString());
    }

    [Fact]
    public async Task UnrelatedInvalidOperationRemains500()
    {
        var context = await InvokeAsync(new InvalidOperationException("unexpected programming error"));
        Assert.Equal(500, context.Response.StatusCode);
    }

    private static async Task<DefaultHttpContext> InvokeAsync(Exception error)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(_ => Task.FromException(error), NullLogger<ExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        return context;
    }
}
