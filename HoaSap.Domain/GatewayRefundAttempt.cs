namespace HoaSap.Domain;

public sealed class GatewayRefundAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RefundId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Processing";
    public string ProviderTransactionCode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
