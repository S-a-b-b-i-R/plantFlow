using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantFlow.Api.Domain.Auditing;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(60);
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.VolumeUnit).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.RequireSurveyBeforePlanning).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PumpFreeAboveCft).HasPrecision(12, 2);
        // b.Property(x => x.MinMarginPercent).HasPrecision(5, 2);
        // The default makes existing companies get 1 when the column is added. The sentinel lets a real 0 (rule off) be saved.
        b.Property(x => x.PumpRestShifts).HasDefaultValue(1).HasSentinel(-1);
        b.Property(x => x.TruckLoadCft).HasPrecision(8, 2);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FirebaseUid).HasMaxLength(128);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => x.FirebaseUid).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.HasKey(x => new { x.RoleId, x.PermissionKey });
        b.Property(x => x.PermissionKey).HasMaxLength(100);
        b.HasIndex(x => x.TenantId);
        b.HasOne(x => x.Role).WithMany(r => r.Permissions).HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasIndex(x => x.TenantId);
        b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b)
    {
        b.HasKey(x => new { x.UserId, x.PermissionKey });
        b.Property(x => x.PermissionKey).HasMaxLength(100);
        b.HasIndex(x => x.TenantId);
        b.HasOne(x => x.User).WithMany(u => u.Permissions).HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
    }
}