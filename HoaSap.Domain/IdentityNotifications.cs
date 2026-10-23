using System.ComponentModel.DataAnnotations;

namespace HoaSap.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    [MaxLength(128)] public string TokenHash { get; set; } = "";
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? RevocationReason { get; set; }
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    [MaxLength(128)] public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
}

public enum NotificationChannel { Email, Sms }
public enum NotificationStatus { Pending, Processing, Sent, Failed, Disabled }

public sealed class NotificationOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(80)] public string EventType { get; set; } = "";
    public Guid? CustomerId { get; set; }
    [MaxLength(254)] public string? Destination { get; set; }
    public NotificationChannel Channel { get; set; }
    [MaxLength(200)] public string Subject { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeasedUntilUtc { get; set; }
    [MaxLength(200)] public string? LeaseOwner { get; set; }
    public DateTime? SentAtUtc { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
