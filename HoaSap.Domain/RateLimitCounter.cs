namespace HoaSap.Domain;

/// <summary>One bounded, database-backed fixed-window bucket. Key is a one-way identifier, never raw credentials.</summary>
public sealed class RateLimitCounter
{
    public Guid Id { get; set; }
    public string BucketKey { get; set; } = "";
    public DateTime WindowStartUtc { get; set; }
    public int Count { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
