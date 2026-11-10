using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Domain.Identity;

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

public class SalesRepConfiguration : IEntityTypeConfiguration<SalesRep>
{
    public void Configure(EntityTypeBuilder<SalesRep> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Region).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // One login user can be linked to at most one rep.
        builder.HasIndex(r => r.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesRep>().WithMany().HasForeignKey(r => r.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();

        builder.HasOne(c => c.SalesRep).WithMany().HasForeignKey(c => c.SalesRepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.Latitude).HasPrecision(9, 6);
        builder.Property(s => s.Longitude).HasPrecision(9, 6);
        builder.Property(s => s.ContactName).HasMaxLength(200);
        builder.Property(s => s.ContactPhone).HasMaxLength(30);
        builder.Property(s => s.AccessNotes).HasMaxLength(1000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(s => new { s.CustomerId, s.Name }).IsUnique();

        builder.HasOne(s => s.Customer).WithMany(c => c.Sites)
            .HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}