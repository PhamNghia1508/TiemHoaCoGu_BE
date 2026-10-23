using HoaSap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoaSap.DAL.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique(); b.HasIndex(x => new { x.CustomerId, x.FamilyId });
        b.Property(x => x.RevocationReason).HasMaxLength(80); b.Property(x => x.ReplacedByHash).HasMaxLength(128);
    }
}
public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired(); b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.ExpiresAtUtc });
    }
}
public sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.EventType).HasMaxLength(80).IsRequired(); b.Property(x => x.Destination).HasMaxLength(254);
        b.Property(x => x.Subject).HasMaxLength(200); b.Property(x => x.LeaseOwner).HasMaxLength(200); b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc }); b.HasIndex(x => x.LeasedUntilUtc);
    }
}
