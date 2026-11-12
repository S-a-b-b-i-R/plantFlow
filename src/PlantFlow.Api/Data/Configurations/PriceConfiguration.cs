using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.Pricing;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PricePerCft).HasPrecision(12, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RejectionReason).HasMaxLength(500);

        // Date ranges cannot be checked by a unique index, so overlaps are checked when a price is approved.
        builder.HasIndex(p => new { p.TenantId, p.ProductId, p.Status });

        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Plant).WithMany().HasForeignKey(p => p.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}