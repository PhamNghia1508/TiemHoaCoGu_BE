using HoaSap.BLL;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Middleware;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected; do not fabricate an internal error or write to a closed response.
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted) throw;
            var status = ex switch
            {
                BusinessException b => b.StatusCode,
                DbUpdateException => 409,
                SqliteException { SqliteErrorCode: 5 or 6 } => 409,
                SqlException { Number: 1205 } => 409,
                _ => 500
            };
            if (status == 500) logger.LogError(ex, "Request failed: {TraceId}", context.TraceIdentifier);
            var detail = ex is BusinessException ? ex.Message : status == 409
                ? "Dữ liệu xung đột hoặc đã được cập nhật. Vui lòng thử lại."
                : "Đã xảy ra lỗi xử lý yêu cầu.";
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = detail,
                Instance = context.Request.Path,
                Extensions = { ["traceId"] = context.TraceIdentifier }
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
