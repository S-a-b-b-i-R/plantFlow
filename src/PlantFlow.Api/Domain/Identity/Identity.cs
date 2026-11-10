using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Domain.Identity;

public enum UserStatus { Pending, Active, Rejected, Disabled }

public class User : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string FirebaseUid { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
    public List<UserPermission> Permissions { get; set; } = [];
}

public class Role : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Name { get; set; }
    public List<RolePermission> Permissions { get; set; } = [];
    public List<UserRole> UserRoles { get; set; } = [];
}

public class RolePermission : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public required string PermissionKey { get; set; }
}

public class UserRole : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

// A per-user override on top of roles: IsGranted = false is an explicit deny.
public class UserPermission : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string PermissionKey { get; set; }
    public bool IsGranted { get; set; } = true;
}