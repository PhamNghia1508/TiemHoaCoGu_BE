using HoaSap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace HoaSap.DAL.Configurations;
public sealed class GatewayPaymentIntentConfiguration : IEntityTypeConfiguration<GatewayPaymentIntent>
{
 public void Configure(EntityTypeBuilder<GatewayPaymentIntent> b)
 {
  b.ToTable("GatewayPaymentIntents", t => t.HasCheckConstraint("CK_GatewayIntent_Amount", "Amount > 0"));
  b.HasKey(x=>x.Id); b.Property(x=>x.EvidenceReference).HasMaxLength(500); b.Property(x=>x.ClientKey).HasMaxLength(100).IsRequired(); b.HasIndex(x=>new {x.OrderId,x.ClientKey}).IsUnique(); b.HasIndex(x=>x.TransactionCode).IsUnique().HasFilter("[TransactionCode] <> ''"); b.Property(x=>x.ProviderKey).HasMaxLength(200).IsRequired(); b.Property(x=>x.TransactionCode).HasMaxLength(100);
  b.Property(x=>x.Currency).HasMaxLength(3).IsRequired(); b.Property(x=>x.ReturnUrlOrigin).HasMaxLength(2048).IsRequired(); b.Property(x=>x.RedirectUrl).HasMaxLength(2048); b.Property(x=>x.Status).HasMaxLength(40).IsRequired(); b.Property(x=>x.ReconciliationStatus).HasMaxLength(80); b.Property(x=>x.ProviderResponse).HasMaxLength(10000); b.Property(x=>x.Amount).HasPrecision(18,2);
  b.HasIndex(x=>x.ProviderKey).IsUnique(); b.HasIndex(x=>new {x.OrderId,x.OwnerId}); b.HasOne<HoaSap.Domain.Entities.Order>().WithMany().HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
 }
}
