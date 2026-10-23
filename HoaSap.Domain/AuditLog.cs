namespace HoaSap.Domain;

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public string OldValuesJson { get; set; } = "{}";
    public string NewValuesJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ActorId { get; set; }
    public string? ActorRole { get; set; }
    public string? TraceId { get; set; }
}
