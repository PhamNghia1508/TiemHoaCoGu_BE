using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using HoaSap.DAL;
using HoaSap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HoaSap.API.Services;

public sealed class NotificationOptions
{
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public int LeaseSeconds { get; set; } = 120;
    public SmtpOptions Smtp { get; set; } = new();
    public SmsOptions Sms { get; set; } = new();
}
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}
public sealed class SmsOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Sender { get; set; } = "";
}

public interface INotificationTransport
{
    NotificationChannel Channel { get; }
    bool Enabled { get; }
    Task SendAsync(NotificationOutbox message, CancellationToken ct);
}
public sealed class SmtpNotificationTransport(IOptions<NotificationOptions> settings) : INotificationTransport
{
    private readonly NotificationOptions options = settings.Value;
    public NotificationChannel Channel => NotificationChannel.Email;
    public bool Enabled => options.EmailEnabled && !string.IsNullOrWhiteSpace(options.Smtp.Host) && !string.IsNullOrWhiteSpace(options.Smtp.From);
    public async Task SendAsync(NotificationOutbox message, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("Email transport is disabled");
        using var client = new SmtpClient(options.Smtp.Host, options.Smtp.Port) { EnableSsl = options.Smtp.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrWhiteSpace(options.Smtp.Username)) client.Credentials = new NetworkCredential(options.Smtp.Username, options.Smtp.Password);
        using var mail = new MailMessage(options.Smtp.From, message.Destination!, message.Subject, RenderBody(message.PayloadJson));
        mail.Headers.Add("Message-Id", $"<{message.Id:N}@hoasap.local>");
        await client.SendMailAsync(mail, ct);
    }
    internal static string RenderBody(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("recoveryUrl", out var url) ? $"Vui lòng mở liên kết khôi phục: {url.GetString()}" : $"Tiệm Hoa Có Gu: {doc.RootElement.GetProperty("code").GetString()} — trạng thái: {doc.RootElement.GetProperty("status").GetString()}."; }
        catch (JsonException) { return "Có cập nhật mới từ Tiệm Hoa Có Gu."; }
    }
}
public sealed class HttpsSmsNotificationTransport(IHttpClientFactory clients, IOptions<NotificationOptions> settings) : INotificationTransport
{
    private readonly NotificationOptions options = settings.Value;
    public NotificationChannel Channel => NotificationChannel.Sms;
    public bool Enabled => options.SmsEnabled && Uri.TryCreate(options.Sms.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(options.Sms.ApiKey);
    public async Task SendAsync(NotificationOutbox message, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("SMS transport is disabled");
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Sms.Endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Sms.ApiKey);
        request.Headers.Add("Idempotency-Key", message.Id.ToString("N"));
        request.Content = JsonContent.Create(new { to = message.Destination, sender = options.Sms.Sender, text = SmtpNotificationTransport.RenderBody(message.PayloadJson) });
        using var response = await clients.CreateClient("notifications").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, IEnumerable<INotificationTransport> transports, IOptions<NotificationOptions> options, ILogger<NotificationWorker> log) : BackgroundService
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatch(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception ex) { log.LogError(ex, "Notification worker cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
    private async Task ProcessBatch(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
        var now = DateTime.UtcNow; var lease = now.AddSeconds(Math.Clamp(options.Value.LeaseSeconds, 15, 3600));
        var enabledChannels = transports.Where(x => x.Enabled).Select(x => x.Channel).ToArray();
        if (enabledChannels.Length == 0) return;
        var candidates = await db.Set<NotificationOutbox>().AsNoTracking().Where(x => enabledChannels.Contains(x.Channel) && (x.Status == NotificationStatus.Pending || (x.Status == NotificationStatus.Processing && x.LeasedUntilUtc < now)) && x.NextAttemptAtUtc <= now).OrderBy(x => x.CreatedAtUtc).Take(50).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in candidates)
        {
            var peek = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var transport = transports.FirstOrDefault(x => x.Channel == peek.Channel);
            if (transport is null || !transport.Enabled) continue; // disabled remains Pending and does not consume attempts
            now = DateTime.UtcNow; lease = now.AddSeconds(Math.Clamp(options.Value.LeaseSeconds, 60, 3600));
            var claimed = await db.Set<NotificationOutbox>().Where(x => x.Id == id && (x.Status == NotificationStatus.Pending || (x.Status == NotificationStatus.Processing && x.LeasedUntilUtc < now)) && x.NextAttemptAtUtc <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, NotificationStatus.Processing).SetProperty(x => x.LeaseOwner, owner).SetProperty(x => x.LeasedUntilUtc, lease), ct);
            if (claimed != 1) continue;
            var row = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deliveryTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(45, Math.Clamp(options.Value.LeaseSeconds, 60, 3600) - 10)));
            try { await transport.SendAsync(row, deliveryTimeout.Token); row.Status = NotificationStatus.Sent; row.SentAtUtc = DateTime.UtcNow; row.LeasedUntilUtc = null; row.LeaseOwner = null; }
            catch (Exception) when (!ct.IsCancellationRequested)
            { row.Attempts++; row.LastError = "delivery failed"; row.Status = row.Attempts >= Math.Max(1, options.Value.MaxAttempts) ? NotificationStatus.Failed : NotificationStatus.Pending; row.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, row.Attempts)))); row.LeasedUntilUtc = null; row.LeaseOwner = null; }
            await db.Set<NotificationOutbox>().Where(x => x.Id == id && x.LeaseOwner == owner && x.Status == NotificationStatus.Processing).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, row.Status).SetProperty(x => x.SentAtUtc, row.SentAtUtc).SetProperty(x => x.Attempts, row.Attempts).SetProperty(x => x.LastError, row.LastError).SetProperty(x => x.NextAttemptAtUtc, row.NextAttemptAtUtc).SetProperty(x => x.LeasedUntilUtc, (DateTime?)null).SetProperty(x => x.LeaseOwner, (string?)null), ct);
        }
    }
}
