using HoaSap.Domain;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoaSap.DAL.Configurations;

public sealed class GatewayRefundAttemptConfiguration : IEntityTypeConfiguration<GatewayRefundAttempt>
{
    public void Configure(EntityTypeBuilder<GatewayRefundAttempt> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.RefundId).IsUnique();
        b.HasIndex(x => x.PaymentId);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Status).HasMaxLength(40);
        b.Property(x => x.ProviderTransactionCode).HasMaxLength(100);
        b.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
