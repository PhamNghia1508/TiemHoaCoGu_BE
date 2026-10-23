namespace HoaSap.Domain;

public sealed class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 300;
    public int WindowSeconds { get; set; } = 60;
    public int AuthPermitLimit { get; set; } = 15;
    public int AuthWindowSeconds { get; set; } = 60;
    public int MaxBuckets { get; set; } = 100_000;
    public int CleanupBatchSize { get; set; } = 500;
    public bool TrustForwardedHeaders { get; set; }
    public string[] TrustedProxies { get; set; } = [];
}
