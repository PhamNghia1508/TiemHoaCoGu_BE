using HoaSap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace HoaSap.DAL.Configurations;
public sealed class RateLimitCounterConfiguration : IEntityTypeConfiguration<RateLimitCounter>
{
    public void Configure(EntityTypeBuilder<RateLimitCounter> b)
    {
        b.ToTable("RateLimitCounters"); b.HasKey(x => x.Id);
        b.Property(x => x.BucketKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.Count).IsRequired(); b.HasIndex(x => new { x.BucketKey, x.WindowStartUtc }).IsUnique();
        b.HasIndex(x => x.UpdatedAtUtc);
    }
}
