using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class PlantConfiguration : IEntityTypeConfiguration<Plant>
{
    public void Configure(EntityTypeBuilder<Plant> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(p => p.OperatingHoursPerDay).HasPrecision(4, 1);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();

        // Restrict, so SQL Server never sees multiple cascade paths.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BatchingPlantConfiguration : IEntityTypeConfiguration<BatchingPlant>
{
    public void Configure(EntityTypeBuilder<BatchingPlant> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Make).HasMaxLength(100);
        builder.Property(b => b.Model).HasMaxLength(100);
        builder.Property(b => b.RatedCapacityCftPerHour).HasPrecision(10, 2);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(b => new { b.PlantId, b.Code }).IsUnique();

        builder.HasOne(b => b.Plant).WithMany(p => p.BatchingPlants)
            .HasForeignKey(b => b.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}