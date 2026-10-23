namespace HoaSap.DAL.Auditing;

public interface IAuditActor
{
    Guid? ActorId { get; }
    string? Role { get; }
    string? TraceId { get; }
}
