using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class MaterialCostConfiguration : IEntityTypeConfiguration<MaterialCost>
{
    public void Configure(EntityTypeBuilder<MaterialCost> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CostPerUnit).HasPrecision(14, 4);
        builder.HasIndex(c => new { c.TenantId, c.MaterialId, c.PlantId, c.EffectiveFrom }).IsUnique();

        builder.HasOne(c => c.Material).WithMany().HasForeignKey(c => c.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Plant).WithMany().HasForeignKey(c => c.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OverheadCostConfiguration : IEntityTypeConfiguration<OverheadCost>
{
    public void Configure(EntityTypeBuilder<OverheadCost> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.AmountPerCft).HasPrecision(12, 4);

        builder.HasOne(c => c.Plant).WithMany().HasForeignKey(c => c.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DeliveryChargeBandConfiguration : IEntityTypeConfiguration<DeliveryChargeBand>
{
    public void Configure(EntityTypeBuilder<DeliveryChargeBand> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.FromKm).HasPrecision(8, 2);
        builder.Property(b => b.ToKm).HasPrecision(8, 2);
        builder.Property(b => b.ChargePerCft).HasPrecision(12, 4);

        builder.HasOne(b => b.Plant).WithMany().HasForeignKey(b => b.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PricingPolicyConfiguration : IEntityTypeConfiguration<PricingPolicy>
{
    public void Configure(EntityTypeBuilder<PricingPolicy> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.VatPercent).HasPrecision(5, 2);
        builder.Property(p => p.MinMarginPercent).HasPrecision(5, 2);
        builder.HasIndex(p => p.TenantId).IsUnique();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AitRateConfiguration : IEntityTypeConfiguration<AitRate>
{
    public void Configure(EntityTypeBuilder<AitRate> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CustomerType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Percent).HasPrecision(5, 2);
        builder.HasIndex(r => new { r.PricingPolicyId, r.CustomerType }).IsUnique();

        builder.HasOne(r => r.PricingPolicy).WithMany(p => p.AitRates).HasForeignKey(r => r.PricingPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}