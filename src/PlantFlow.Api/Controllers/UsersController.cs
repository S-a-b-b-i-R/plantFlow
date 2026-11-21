using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record UserDto(
    Guid Id, string Email, string? DisplayName, UserStatus Status, bool IsPlatformAdmin,
    List<string> Roles, DateTime? ApprovedAtUtc, List<UserPlantDto> Plants);

public record UserPlantDto(Guid Id, string Code, string Name);

public record PermissionOverrideDto(string PermissionKey, bool IsGranted);

public record UserDetailDto(
    Guid Id, string Email, string? DisplayName, UserStatus Status,
    List<Guid> RoleIds, List<PermissionOverrideDto> PermissionOverrides, List<Guid> PlantIds);

public class SetRolesRequest
{
    public List<Guid> RoleIds { get; init; } = [];
}

public class SetPlantsRequest
{
    public List<Guid> PlantIds { get; init; } = [];
}

public class SetPermissionOverridesRequest
{
    public List<PermissionOverrideDto> Overrides { get; init; } = [];
}

public class DisableUserRequest
{
    [StringLength(500)]
    public string? Reason { get; init; }
}

public record PendingCountDto(int Count);

// Every query here is limited to the signed-in user's company by the tenant filter.
[ApiController]
[Route("api/users")]
[HasPermission(Permissions.UsersManage)]
public class UsersController(
    PlantFlowDbContext db,
    ICurrentUser currentUser,
    PermissionService permissionService,
    AuditWriter audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List(CancellationToken cancellationToken)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserDto(
                u.Id, u.Email, u.DisplayName, u.Status, u.IsPlatformAdmin,
                u.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n).ToList(),
                u.ApprovedAtUtc,
                u.UserPlants.OrderBy(up => up.Plant.Code)
                    .Select(up => new UserPlantDto(up.PlantId, up.Plant.Code, up.Plant.Name)).ToList()))
            .ToListAsync(cancellationToken);
    }

    // For the badge on Users in the menu.
    [HttpGet("pending-count")]
    public async Task<ActionResult<PendingCountDto>> PendingCount(CancellationToken cancellationToken) =>
        new PendingCountDto(await db.Users.CountAsync(u => u.Status == UserStatus.Pending, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDetailDto(
                u.Id, u.Email, u.DisplayName, u.Status,
                u.UserRoles.Select(ur => ur.RoleId).ToList(),
                u.Permissions.Select(p => new PermissionOverrideDto(p.PermissionKey, p.IsGranted)).ToList(),
                u.UserPlants.Select(up => up.PlantId).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return NotFound();
        return user;
    }

    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, UserStatus.Active, [UserStatus.Pending, UserStatus.Rejected], "user.approved", cancellationToken);

    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, UserStatus.Rejected, [UserStatus.Pending], "user.rejected", cancellationToken);

    // Disabling someone who worked here needs a reason, and the company always keeps one active Admin.
    [HttpPost("{id:guid}/disable")]
    public Task<IActionResult> Disable(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DisableUserRequest? request,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, UserStatus.Disabled, [UserStatus.Active], "user.disabled", cancellationToken,
            reason: request?.Reason?.Trim() ?? "", requireReason: true);

    [HttpPost("{id:guid}/enable")]
    public Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, UserStatus.Active, [UserStatus.Disabled], "user.enabled", cancellationToken);

    // Replaces the user's roles with exactly this set.
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken cancellationToken)
    {
        if (id == currentUser.UserId) return CannotChangeSelf();

        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return NotFound();

        var wanted = request.RoleIds.Distinct().ToList();
        var validCount = await db.Roles.CountAsync(r => wanted.Contains(r.Id), cancellationToken);
        if (validCount != wanted.Count)
        {
            return Problem(title: "One or more roles do not exist in this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        var keepsAdmin = await db.Roles.AnyAsync(
            r => wanted.Contains(r.Id) && r.SystemKey == DefaultRoles.AdminKey, cancellationToken);
        if (!keepsAdmin && await IsLastAdminAsync(user.Id, cancellationToken)) return LastAdmin();

        db.UserRoles.RemoveRange(user.UserRoles.Where(ur => !wanted.Contains(ur.RoleId)));
        foreach (var roleId in wanted.Where(r => user.UserRoles.All(ur => ur.RoleId != r)))
        {
            db.UserRoles.Add(new UserRole { TenantId = user.TenantId, UserId = user.Id, RoleId = roleId });
        }

        audit.Record(user.TenantId, "user.roles_changed", "User", user.Id.ToString(), new { RoleIds = wanted });
        await db.SaveChangesAsync(cancellationToken);

        permissionService.Invalidate(user.Id, user.TenantId);
        return NoContent();
    }

    // Replaces the plants the user works for with exactly this set.
    [HttpPut("{id:guid}/plants")]
    public async Task<IActionResult> SetPlants(Guid id, SetPlantsRequest request, CancellationToken cancellationToken)
    {
        if (id == currentUser.UserId) return CannotChangeSelf();

        var user = await db.Users.Include(u => u.UserPlants).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return NotFound();

        var wanted = request.PlantIds.Distinct().ToList();
        var plants = await db.Plants.AsNoTracking()
            .Where(p => wanted.Contains(p.Id))
            .Select(p => new { p.Id, p.Status })
            .ToListAsync(cancellationToken);
        if (plants.Count != wanted.Count)
        {
            return Problem(title: "One or more plants do not exist in this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        // A plant that was deactivated can stay on the user, but cannot be newly given.
        var current = user.UserPlants.Select(up => up.PlantId).ToHashSet();
        if (plants.Any(p => p.Status != ActiveStatus.Active && !current.Contains(p.Id)))
        {
            return Problem(
                title: "An inactive plant cannot be assigned. Reactivate the plant first.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        db.UserPlants.RemoveRange(user.UserPlants.Where(up => !wanted.Contains(up.PlantId)));
        foreach (var plantId in wanted.Where(p => !current.Contains(p)))
        {
            db.UserPlants.Add(new UserPlant { TenantId = user.TenantId, UserId = user.Id, PlantId = plantId });
        }

        audit.Record(user.TenantId, "user.plants_changed", "User", user.Id.ToString(), new { PlantIds = wanted });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Replaces the user's own grants and denies with exactly this set. A deny beats a role's permission.
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissionOverrides(
        Guid id, SetPermissionOverridesRequest request, CancellationToken cancellationToken)
    {
        if (id == currentUser.UserId) return CannotChangeSelf();

        var unknown = request.Overrides.Any(o => !Permissions.All.Contains(o.PermissionKey ?? ""));
        var duplicated = request.Overrides.GroupBy(o => o.PermissionKey).Any(g => g.Count() > 1);
        if (unknown || duplicated)
        {
            return Problem(
                title: "Permission keys must be known and listed only once.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await db.Users.Include(u => u.Permissions).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return NotFound();

        var deniesUsers = request.Overrides.Any(o => o.PermissionKey == Permissions.UsersManage && !o.IsGranted);
        if (deniesUsers && await IsLastAdminAsync(user.Id, cancellationToken)) return LastAdmin();

        // Update in place so we never delete and re-insert the same key in one save.
        var existing = user.Permissions.ToDictionary(p => p.PermissionKey);
        foreach (var wanted in request.Overrides)
        {
            if (existing.TryGetValue(wanted.PermissionKey, out var current))
            {
                current.IsGranted = wanted.IsGranted;
            }
            else
            {
                db.UserPermissions.Add(new UserPermission
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    PermissionKey = wanted.PermissionKey,
                    IsGranted = wanted.IsGranted
                });
            }
        }

        var wantedKeys = request.Overrides.Select(o => o.PermissionKey).ToHashSet();
        db.UserPermissions.RemoveRange(user.Permissions.Where(p => !wantedKeys.Contains(p.PermissionKey)));

        audit.Record(user.TenantId, "user.permissions_changed", "User", user.Id.ToString(),
            new { Overrides = request.Overrides });
        await db.SaveChangesAsync(cancellationToken);

        permissionService.Invalidate(user.Id, user.TenantId);
        return NoContent();
    }

    private async Task<IActionResult> ChangeStatusAsync(
        Guid id, UserStatus target, UserStatus[] allowedFrom, string action, CancellationToken cancellationToken,
        string? reason = null, bool requireReason = false)
    {
        if (id == currentUser.UserId) return CannotChangeSelf();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return NotFound();

        if (!allowedFrom.Contains(user.Status))
        {
            return Problem(
                title: $"A {user.Status} user cannot be changed to {target}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (requireReason && string.IsNullOrEmpty(reason))
        {
            return Problem(title: ReasonNeeded, statusCode: StatusCodes.Status400BadRequest);
        }

        if (target != UserStatus.Active && await IsLastAdminAsync(user.Id, cancellationToken)) return LastAdmin();

        var from = user.Status;
        user.Status = target;
        if (target == UserStatus.Active && user.ApprovedAtUtc is null)
        {
            user.ApprovedBy = currentUser.UserId;
            user.ApprovedAtUtc = DateTime.UtcNow;
        }

        audit.Record(user.TenantId, action, "User", user.Id.ToString(),
            new { From = from.ToString(), To = target.ToString(), Reason = string.IsNullOrEmpty(reason) ? null : reason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public const string ReasonNeeded = "Give a reason for disabling this user.";

    public const string LastAdminMessage =
        "This is the company's only active Admin. Make someone else an Admin first.";

    // Active, holding the Admin role, and not denied user management.
    private IQueryable<User> ActiveAdmins() =>
        db.Users.Where(u => u.Status == UserStatus.Active
            && u.UserRoles.Any(ur => ur.Role.SystemKey == DefaultRoles.AdminKey)
            && !u.Permissions.Any(p => p.PermissionKey == Permissions.UsersManage && !p.IsGranted));

    // True when this user is the company's only active Admin, so losing Admin would lock the company out.
    private async Task<bool> IsLastAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        await ActiveAdmins().AnyAsync(u => u.Id == userId, cancellationToken)
        && !await ActiveAdmins().AnyAsync(u => u.Id != userId, cancellationToken);

    private ObjectResult LastAdmin() =>
        Problem(title: LastAdminMessage, statusCode: StatusCodes.Status409Conflict);

    private ObjectResult CannotChangeSelf() =>
        Problem(title: "You cannot change your own account.", statusCode: StatusCodes.Status409Conflict);
}