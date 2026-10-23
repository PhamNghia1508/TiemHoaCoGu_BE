namespace HoaSap.Domain.Entities;

/// <summary>Liên kết một lần checkout với đúng revision; dùng cho idempotency.</summary>
public class CustomOrderCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomQuotationId { get; set; }
    public int QuotationVersion { get; set; }
    public Guid CustomerId { get; set; }
    public Guid OrderId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
