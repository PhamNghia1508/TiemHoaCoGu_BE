namespace HoaSap.Domain.Entities;

public class CustomQuotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomOrderRequestId { get; set; }
    public int CurrentRevision { get; set; }
    public string Status { get; set; } = "AwaitingQuote";
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
