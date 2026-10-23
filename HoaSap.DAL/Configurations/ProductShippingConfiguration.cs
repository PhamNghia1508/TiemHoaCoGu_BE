using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoaSap.DAL.Configurations;

public sealed class ProductShippingConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.ShippingWeightGrams).HasDefaultValue(500);
        b.ToTable("Products", t => t.HasCheckConstraint("CK_Product_ShippingWeight", "ShippingWeightGrams > 0"));
    }
}
