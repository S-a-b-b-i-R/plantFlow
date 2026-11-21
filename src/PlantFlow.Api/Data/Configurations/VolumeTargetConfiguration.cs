using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.Targets;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class VolumeTargetConfiguration : IEntityTypeConfiguration<VolumeTarget>
{
    public void Configure(EntityTypeBuilder<VolumeTarget> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Level).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.VolumeCft).HasPrecision(14, 2);

        // One target per company, plant or rep per quarter. SQL Server treats the empty ids as equal here.
        builder.HasIndex(t => new { t.TenantId, t.Year, t.Quarter, t.Level, t.PlantId, t.SalesRepId }).IsUnique().HasFilter(null);

        builder.HasOne(t => t.Plant).WithMany().HasForeignKey(t => t.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.SalesRep).WithMany().HasForeignKey(t => t.SalesRepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
