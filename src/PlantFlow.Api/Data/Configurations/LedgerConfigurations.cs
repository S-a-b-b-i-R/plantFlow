using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.Ledger;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class LedgerCreditConfiguration : IEntityTypeConfiguration<LedgerCredit>
{
    public void Configure(EntityTypeBuilder<LedgerCredit> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UnitPrice).HasPrecision(12, 2);
        builder.Property(c => c.OriginalCft).HasPrecision(12, 2);
        builder.Property(c => c.BalanceCft).HasPrecision(12, 2);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => new { c.TenantId, c.CustomerId, c.ProductId });
        // A short-closed line carries forward once.
        builder.HasIndex(c => c.SourceOrderLineId).IsUnique().HasFilter("[SourceOrderLineId] IS NOT NULL");

        builder.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SourceOrder).WithMany().HasForeignKey(c => c.SourceOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Orders.OrderLine>().WithMany().HasForeignKey(c => c.SourceOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LedgerCreditUseConfiguration : IEntityTypeConfiguration<LedgerCreditUse>
{
    public void Configure(EntityTypeBuilder<LedgerCreditUse> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.QuantityCft).HasPrecision(12, 2);
        builder.HasIndex(u => u.OrderId);

        builder.HasOne(u => u.Credit).WithMany(c => c.Uses).HasForeignKey(u => u.CreditId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.Order).WithMany().HasForeignKey(u => u.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.OrderLine).WithMany().HasForeignKey(u => u.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LedgerCorrectionConfiguration : IEntityTypeConfiguration<LedgerCorrection>
{
    public void Configure(EntityTypeBuilder<LedgerCorrection> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.QuantityCft).HasPrecision(12, 2);
        builder.Property(c => c.Reason).HasMaxLength(500).IsRequired();

        builder.HasOne(c => c.Credit).WithMany(c => c.Corrections).HasForeignKey(c => c.CreditId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LedgerCloseConfiguration : IEntityTypeConfiguration<LedgerClose>
{
    public void Configure(EntityTypeBuilder<LedgerClose> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.QuantityCft).HasPrecision(12, 2);
        builder.Property(c => c.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(500).IsRequired();
        builder.Property(c => c.ReviewComment).HasMaxLength(500);
        builder.HasIndex(c => new { c.TenantId, c.Status });

        builder.HasOne(c => c.Credit).WithMany(c => c.Closes).HasForeignKey(c => c.CreditId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
