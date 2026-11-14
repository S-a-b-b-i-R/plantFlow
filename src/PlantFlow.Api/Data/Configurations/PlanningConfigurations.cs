using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class SiteSurveyConfiguration : IEntityTypeConfiguration<SiteSurvey>
{
    public void Configure(EntityTypeBuilder<SiteSurvey> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Result).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.HasIndex(s => new { s.SiteId, s.SurveyedOn });

        builder.HasOne(s => s.Site).WithMany().HasForeignKey(s => s.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
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