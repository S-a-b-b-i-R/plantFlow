using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class SitePlantDistanceConfiguration : IEntityTypeConfiguration<SitePlantDistance>
{
    public void Configure(EntityTypeBuilder<SitePlantDistance> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DistanceKm).HasPrecision(8, 2);
        builder.HasIndex(d => new { d.SiteId, d.PlantId }).IsUnique();

        builder.HasOne(d => d.Site).WithMany().HasForeignKey(d => d.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Plant).WithMany().HasForeignKey(d => d.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.DistanceKm).HasPrecision(8, 2);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.PaymentTerms).HasMaxLength(100);
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.CancelReason).HasMaxLength(500);

        builder.HasIndex(o => new { o.TenantId, o.Sequence }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });

        builder.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Site).WithMany().HasForeignKey(o => o.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.SalesRep).WithMany().HasForeignKey(o => o.SalesRepId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Plant).WithMany().HasForeignKey(o => o.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.QuantityCft).HasPrecision(12, 2);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.FloorPrice).HasPrecision(12, 2);
        builder.Property(l => l.PourElement).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.PourMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Notes).HasMaxLength(500);

        builder.HasOne(l => l.Order).WithMany(o => o.Lines).HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.MixDesign).WithMany().HasForeignKey(l => l.MixDesignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Price).WithMany().HasForeignKey(l => l.PriceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).HasMaxLength(500);

        builder.HasOne(h => h.Order).WithMany(o => o.History).HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(h => h.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderApprovalConfiguration : IEntityTypeConfiguration<OrderApproval>
{
    public void Configure(EntityTypeBuilder<OrderApproval> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Stage).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Comment).HasMaxLength(500);
        builder.HasIndex(a => new { a.OrderId, a.Round });

        builder.HasOne(a => a.Order).WithMany(o => o.Approvals).HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}