using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace HoaSap.BLL.Commerce;

public class CommerceGatewayOptions
{
    public string? Endpoint { get; set; }
    public string? Secret { get; set; }
    public string? MerchantId { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public string[] AllowedReturnOrigins { get; set; } = [];
    public string[] ReturnUrlAllowlist { get => AllowedReturnOrigins; set => AllowedReturnOrigins = value ?? []; }
    public virtual void Validate()
    {
        ValidateTransport();
        if (AllowedReturnOrigins.Length == 0 || AllowedReturnOrigins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.PathAndQuery.Trim('/'))))
            throw new InvalidOperationException("Commerce gateway return URL allowlist is required and must contain HTTPS origins only.");
    }
    protected void ValidateTransport()
    {
        if (string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Secret) || string.IsNullOrWhiteSpace(MerchantId))
            throw new InvalidOperationException("Commerce gateway is not configured; no payment/refund request was sent.");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Commerce gateway endpoint must be an absolute HTTPS URL.");
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host, out var ip) && (IPAddress.IsLoopback(ip) || IsPrivate(ip)))
            throw new InvalidOperationException("Commerce gateway endpoint cannot target a local or private address.");
        if (TimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("Commerce gateway timeout is invalid.");

    }
    public bool IsAllowedReturnOrigin(Uri uri) => AllowedReturnOrigins.Any(x => Uri.TryCreate(x.TrimEnd('/'), UriKind.Absolute, out var allowed) && string.Equals(allowed.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(allowed.Host, uri.Host, StringComparison.OrdinalIgnoreCase) && allowed.Port == uri.Port);
    static bool IsPrivate(IPAddress ip) { var b = ip.GetAddressBytes(); return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (b[0] == 10 || b[0] == 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168); }
}

public sealed class CarrierOptions : CommerceGatewayOptions { public override void Validate() => ValidateTransport(); public string Origin { get; set; } = ""; public int QuoteLifetimeMinutes { get; set; } = 15; }
public sealed record GatewayChargeRequest(Guid OrderId, decimal Amount, string Currency, string ReturnUrl, string ProviderKey = "");
public sealed record GatewayChargeResult(bool Accepted, string TransactionCode, string? RedirectUrl, string? RawResponse);
public sealed record GatewayRefundRequest(Guid RefundId, decimal Amount, string Currency, string OriginalTransactionCode);
public sealed record GatewayRefundResult(bool Accepted, string TransactionCode, string? RawResponse);
public sealed record CarrierRateRequest(string Origin, string Destination, decimal Weight);
public sealed record CarrierRateResult(bool Accepted, decimal Amount, string? TrackingNumber, string? RawResponse);

public interface IPaymentGateway
{
    Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken ct = default);
    Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken ct = default);
    bool VerifyWebhook(ReadOnlySpan<byte> payload, string? signature, string? timestamp);
}
public interface ICarrierAdapter { Task<CarrierRateResult> QuoteAsync(CarrierRateRequest request, CancellationToken ct = default); }

public static class CommerceServiceCollectionExtensions
{
    public static IServiceCollection AddCommerceAdapters(this IServiceCollection services, Action<CommerceGatewayOptions>? gateway = null, Action<CarrierOptions>? carrier = null)
    {
        services.AddOptions<CommerceGatewayOptions>().Configure(o => gateway?.Invoke(o));
        services.AddOptions<CarrierOptions>().Configure(o => carrier?.Invoke(o));
        services.AddSingleton<IPaymentGateway>(sp => new SignedHttpPaymentGateway(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), sp.GetRequiredService<IOptions<CommerceGatewayOptions>>()));
        services.AddSingleton<ICarrierAdapter>(sp => new SignedHttpCarrier(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), sp.GetRequiredService<IOptions<CarrierOptions>>()));
        services.AddScoped<GatewayPaymentService>();
        services.AddScoped<GatewayRefundService>();
        return services;
    }
}

/// <summary>Generic signed adapter only. It deliberately does not identify or emulate a real provider.</summary>
public sealed class SignedHttpPaymentGateway(HttpClient http, IOptions<CommerceGatewayOptions> options) : IPaymentGateway, IDisposable
{
    public void Dispose() => http.Dispose();
    readonly HttpClient http = http;
    readonly CommerceGatewayOptions cfg = options.Value;
    public async Task<GatewayChargeResult> ChargeAsync(GatewayChargeRequest request, CancellationToken ct = default) => await SendAsync<GatewayChargeRequest, GatewayChargeResult>("charge", request, ct);
    public async Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken ct = default) => await SendAsync<GatewayRefundRequest, GatewayRefundResult>("refund", request, ct);
    async Task<T> SendAsync<TIn, T>(string operation, TIn value, CancellationToken ct)
    {
        cfg.Validate();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));
        var body = JsonSerializer.Serialize(value); var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(cfg.Endpoint!.TrimEnd('/') + "/"), operation)); req.Content = new StringContent(body, Encoding.UTF8, "application/json"); AddSignature(req, body, timestamp);
        using var response = await http.SendAsync(req, cts.Token); var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gateway rejected {operation}: {(int)response.StatusCode}.");
        return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Gateway returned an invalid response.");
    }
    void AddSignature(HttpRequestMessage req, string body, string timestamp) { req.Headers.Add("X-Merchant-Id", cfg.MerchantId); req.Headers.Add("X-Timestamp", timestamp); req.Headers.Add("X-Signature", Sign(body, timestamp)); }
    string Sign(string body, string timestamp) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.Secret!), Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
    public bool VerifyWebhook(ReadOnlySpan<byte> payload, string? signature, string? timestamp)
    {
        try { cfg.Validate(); if (!long.TryParse(timestamp, out var ts) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > 300 || string.IsNullOrWhiteSpace(signature)) return false; var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.Secret!), Encoding.UTF8.GetBytes(timestamp + "." + Encoding.UTF8.GetString(payload)))).ToLowerInvariant(); return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant())); } catch { return false; }
    }
}

public sealed class SignedHttpCarrier(HttpClient http, IOptions<CarrierOptions> options) : ICarrierAdapter, IDisposable
{
    public void Dispose() => http.Dispose();
    readonly HttpClient http = http; readonly CarrierOptions cfg = options.Value;
    public async Task<CarrierRateResult> QuoteAsync(CarrierRateRequest request, CancellationToken ct = default)
    {
        cfg.Validate(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds)); ct = timeout.Token; using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(cfg.Endpoint!.TrimEnd('/') + "/"), "quote")); var body = JsonSerializer.Serialize(request); req.Content = new StringContent(body, Encoding.UTF8, "application/json"); var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); req.Headers.Add("X-Merchant-Id", cfg.MerchantId); req.Headers.Add("X-Timestamp", stamp); req.Headers.Add("X-Signature", Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.Secret!), Encoding.UTF8.GetBytes(stamp + "." + body))).ToLowerInvariant()); using var response = await http.SendAsync(req, ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Carrier rejected quote request."); return await response.Content.ReadFromJsonAsync<CarrierRateResult>(cancellationToken: ct) ?? throw new InvalidOperationException("Carrier returned an invalid response.");
    }
}
