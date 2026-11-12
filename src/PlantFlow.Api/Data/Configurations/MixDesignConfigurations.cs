using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class MixDesignConfiguration : IEntityTypeConfiguration<MixDesign>
{
    public void Configure(EntityTypeBuilder<MixDesign> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.RejectionReason).HasMaxLength(500);

        // The database itself guarantees the rules, so two people acting at once cannot break them.
        // A missing plant counts as its own scope: SQL Server treats NULLs as equal in unique indexes.
        builder.HasIndex(d => new { d.TenantId, d.ProductId, d.PlantId, d.Version })
            .IsUnique().HasFilter("[Kind] = 'Standard'").HasDatabaseName("IX_MixDesigns_Version");

        builder.HasIndex(d => new { d.TenantId, d.ProductId, d.PlantId })
            .IsUnique().HasFilter("[Kind] = 'Standard' AND [Status] = 'Approved'").HasDatabaseName("IX_MixDesigns_OneApproved");

        builder.HasIndex(d => new { d.TenantId, d.ProductId, d.PlantId })
            .IsUnique().HasFilter("[Kind] = 'Standard' AND [Status] IN ('Draft', 'PendingApproval')")
            .HasDatabaseName("IX_MixDesigns_OneOpen");

        builder.HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Plant).WithMany().HasForeignKey(d => d.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MixDesignItemConfiguration : IEntityTypeConfiguration<MixDesignItem>
{
    public void Configure(EntityTypeBuilder<MixDesignItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.QuantityPerCft).HasPrecision(12, 4);

        builder.HasIndex(i => new { i.MixDesignId, i.MaterialId }).IsUnique();

        builder.HasOne(i => i.MixDesign).WithMany(d => d.Items).HasForeignKey(i => i.MixDesignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Material).WithMany().HasForeignKey(i => i.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}