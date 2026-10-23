using HoaSap.BLL.Commerce;

namespace HoaSap.API.Services;

public sealed class OrderExpiryWorker(IServiceScopeFactory scopes, ILogger<OrderExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await using var scope = scopes.CreateAsyncScope(); var count = await scope.ServiceProvider.GetRequiredService<CommerceService>().ExpireUnpaidOrdersAsync(ct: stoppingToken); if (count > 0) logger.LogInformation("Expired {Count} unpaid orders", count); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Order expiry pass failed; it will be retried on the next tick"); }
        }
    }
}
