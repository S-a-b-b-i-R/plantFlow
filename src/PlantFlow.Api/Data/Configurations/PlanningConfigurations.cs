using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Data.Configurations;

public class SiteSurveyConfiguration : IEntityTypeConfiguration<SiteSurvey>
{
    public void Configure(EntityTypeBuilder<SiteSurvey> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Result).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.Latitude).HasPrecision(9, 6);
        builder.Property(s => s.Longitude).HasPrecision(9, 6);
        builder.Property(s => s.AccuracyMeters).HasPrecision(8, 1);
        builder.Property(s => s.PipingMetres).HasPrecision(6, 1);
        builder.Property(s => s.PumpPosition).HasMaxLength(500);
        builder.Property(s => s.TruckConnectionPoint).HasMaxLength(500);
        builder.Property(s => s.LocationNote).HasMaxLength(1000);
        builder.HasIndex(s => new { s.SiteId, s.SurveyedOn });

        builder.HasOne(s => s.Site).WithMany().HasForeignKey(s => s.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        // An order can tag a survey.
        builder.HasMany<Order>().WithOne().HasForeignKey(o => o.SiteSurveyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SiteSurveyImageConfiguration : IEntityTypeConfiguration<SiteSurveyImage>
{
    public void Configure(EntityTypeBuilder<SiteSurveyImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.FileName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(i => i.Caption).HasMaxLength(200);
        builder.Property(i => i.Content).IsRequired();
        builder.HasIndex(i => i.SurveyId);

        builder.HasOne(i => i.Survey).WithMany(s => s.Images).HasForeignKey(i => i.SurveyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PumpCrewMemberConfiguration : IEntityTypeConfiguration<PumpCrewMember>
{
    public void Configure(EntityTypeBuilder<PumpCrewMember> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => new { c.VehicleId, c.Name }).IsUnique();

        builder.HasOne(c => c.Vehicle).WithMany().HasForeignKey(c => c.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShiftPlanConfiguration : IEntityTypeConfiguration<ShiftPlan>
{
    public void Configure(EntityTypeBuilder<ShiftPlan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ReturnReason).HasMaxLength(500);
        builder.HasIndex(p => new { p.PlantId, p.Date, p.ShiftId }).IsUnique();

        builder.HasOne(p => p.Plant).WithMany().HasForeignKey(p => p.PlantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Shift).WithMany().HasForeignKey(p => p.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PourConfiguration : IEntityTypeConfiguration<Pour>
{
    public void Configure(EntityTypeBuilder<Pour> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PlannedCft).HasPrecision(12, 2);
        builder.Property(p => p.PlannedStart).HasColumnType("datetime2(0)");
        builder.Property(p => p.PumpLeaveAt).HasColumnType("datetime2(0)");
        builder.Property(p => p.FirstTruckLeaveAt).HasColumnType("datetime2(0)");
        builder.Property(p => p.CrewNotes).HasMaxLength(500);
        builder.Property(p => p.RestOverrideReason).HasMaxLength(500);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.HasIndex(p => p.PumpId);
        builder.HasIndex(p => p.OrderLineId);

        builder.HasOne(p => p.ShiftPlan).WithMany(s => s.Pours).HasForeignKey(p => p.ShiftPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.OrderLine).WithMany().HasForeignKey(p => p.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Pump).WithMany().HasForeignKey(p => p.PumpId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Operator).WithMany().HasForeignKey(p => p.OperatorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TruckLoadConfiguration : IEntityTypeConfiguration<TruckLoad>
{
    public void Configure(EntityTypeBuilder<TruckLoad> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.QuantityCft).HasPrecision(10, 2);
        builder.Property(l => l.QuantityReason).HasMaxLength(320);
        builder.Property(l => l.CancelReason).HasMaxLength(300);
        builder.HasIndex(l => l.PourId);
        builder.HasIndex(l => l.TruckId);

        builder.HasOne(l => l.Pour).WithMany().HasForeignKey(l => l.PourId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Truck).WithMany().HasForeignKey(l => l.TruckId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}