namespace HoaSap.Domain;

public sealed class GatewayPaymentIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid OwnerId { get; set; }
    public string ClientKey { get; set; } = "";
    public string ProviderKey { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string ReturnUrlOrigin { get; set; } = "";
    public string RedirectUrl { get; set; } = "";
    public string TransactionCode { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string ReconciliationStatus { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public string ProviderResponse { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
