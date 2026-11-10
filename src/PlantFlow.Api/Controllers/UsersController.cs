using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record UserDto(
    Guid Id, string Email, string? DisplayName, UserStatus Status, bool IsPlatformAdmin,
    List<string> Roles, DateTime? ApprovedAtUtc);

public record PermissionOverrideDto(string PermissionKey, bool IsGranted);

public record UserDetailDto(
    Guid Id, string Email, string? DisplayName, UserStatus Status,
    List<Guid> RoleIds, List<PermissionOverrideDto> PermissionOverrides);

public class SetRolesRequest
{
    public List<Guid> RoleIds { get; init; } = [];
}

public class SetPermissionOverridesRequest
{
    public List<PermissionOverrideDto> Overrides { get; init; } = [];
}

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
                u.ApprovedAtUtc))
            .ToListAsync(cancellationToken);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDetailDto(
                u.Id, u.Email, u.DisplayName, u.Status,
                u.UserRoles.Select(ur => ur.RoleId).ToList(),
                u.Permissions.Select(p => new PermissionOverrideDto(p.PermissionKey, p.IsGranted)).ToList()))
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

    [HttpPost("{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, UserStatus.Disabled, [UserStatus.Active], "user.disabled", cancellationToken);

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
        Guid id, UserStatus target, UserStatus[] allowedFrom, string action, CancellationToken cancellationToken)
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

        var from = user.Status;
        user.Status = target;
        if (target == UserStatus.Active && user.ApprovedAtUtc is null)
        {
            user.ApprovedBy = currentUser.UserId;
            user.ApprovedAtUtc = DateTime.UtcNow;
        }

        audit.Record(user.TenantId, action, "User", user.Id.ToString(),
            new { From = from.ToString(), To = target.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private ObjectResult CannotChangeSelf() =>
        Problem(title: "You cannot change your own account.", statusCode: StatusCodes.Status409Conflict);
}