using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// IsBuiltIn: one of PlantFlow's roles, which can be renamed but not deleted. IsLocked: the Admin role,
// which always has every permission and can't be changed at all.
public record RoleDto(
    Guid Id, string Name, List<string> Permissions, bool IsBuiltIn = false, bool IsLocked = false, int UserCount = 0);

public class RoleRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = "";

    public List<string> Permissions { get; init; } = [];
}

[ApiController]
public class RolesController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, PermissionService permissionService)
    : ControllerBase
{
    public const string AdminLocked = "The Admin role always has every permission and can't be changed.";
    public const string NameTaken = "Another role already has that name.";
    public const string BuiltInCannotBeDeleted =
        "PlantFlow's own roles can't be deleted. Rename it or change what it can do instead.";
    public const string UnknownPermission = "One or more permissions are not known.";

    // People who assign roles need the list as much as people who edit roles.
    [HttpGet("api/roles")]
    [HasPermission(Permissions.UsersManage, Permissions.RolesManage)]
    public async Task<ActionResult<List<RoleDto>>> ListRoles(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.SystemKey,
                Keys = r.Permissions.Select(p => p.PermissionKey).ToList(),
                UserCount = r.UserRoles.Count
            })
            .ToListAsync(cancellationToken);

        return roles.Select(r =>
        {
            var locked = r.SystemKey == DefaultRoles.AdminKey;
            var keys = (locked ? Permissions.All : r.Keys.Where(Permissions.All.Contains)).Order().ToList();
            return new RoleDto(r.Id, r.Name, keys, r.SystemKey != null, locked, r.UserCount);
        }).ToList();
    }

    [HttpGet("api/permissions")]
    [HasPermission(Permissions.UsersManage, Permissions.RolesManage)]
    public ActionResult<IReadOnlyList<string>> ListPermissions() => Ok(Permissions.All);

    // A company's own role.
    [HttpPost("api/roles")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<IActionResult> Create(RoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var keys = request.Permissions.Distinct().ToList();
        if (keys.Any(k => !Permissions.All.Contains(k))) return Problem(title: UnknownPermission, statusCode: 400);
        if (await db.Roles.AnyAsync(r => r.Name == name, cancellationToken)) return Problem(title: NameTaken, statusCode: 409);

        var tenantId = currentUser.TenantId!.Value;
        var role = new Role { TenantId = tenantId, Name = name };
        role.Permissions.AddRange(keys.Select(k => new RolePermission { TenantId = tenantId, PermissionKey = k }));
        db.Roles.Add(role);
        audit.Record(tenantId, "role.created", "Role", role.Id.ToString(), new { role.Name, Permissions = keys });
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/roles/{role.Id}", new RoleDto(role.Id, role.Name, keys.Order().ToList()));
    }

    // Renames the role and replaces what it can do. Everyone with the role gets the change at once.
    [HttpPut("api/roles/{id:guid}")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<IActionResult> Update(Guid id, RoleRequest request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null) return NotFound();
        if (role.SystemKey == DefaultRoles.AdminKey) return Problem(title: AdminLocked, statusCode: 409);

        var name = request.Name.Trim();
        var keys = request.Permissions.Distinct().ToList();
        if (keys.Any(k => !Permissions.All.Contains(k))) return Problem(title: UnknownPermission, statusCode: 400);
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id, cancellationToken))
        {
            return Problem(title: NameTaken, statusCode: 409);
        }

        var before = role.Permissions.Select(p => p.PermissionKey).ToHashSet();
        var added = keys.Where(k => !before.Contains(k)).Order().ToList();
        var removed = before.Where(k => !keys.Contains(k)).Order().ToList();
        var oldName = role.Name;

        role.Name = name;
        db.RolePermissions.RemoveRange(role.Permissions.Where(p => removed.Contains(p.PermissionKey)));
        foreach (var key in added)
        {
            db.RolePermissions.Add(new RolePermission { TenantId = role.TenantId, RoleId = role.Id, PermissionKey = key });
        }

        audit.Record(role.TenantId, "role.updated", "Role", role.Id.ToString(),
            new { OldName = oldName, role.Name, Added = added, Removed = removed });
        await db.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateRoleAsync(role.Id, role.TenantId, cancellationToken);
        return NoContent();
    }

    // Only a company's own roles, and only when nobody has them.
    [HttpDelete("api/roles/{id:guid}")]
    [HasPermission(Permissions.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null) return NotFound();
        if (role.SystemKey != null) return Problem(title: BuiltInCannotBeDeleted, statusCode: 409);

        var holders = await db.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);
        if (holders > 0)
        {
            var who = holders == 1 ? "1 person has" : $"{holders} people have";
            return Problem(title: $"{who} this role. Give them another role first.", statusCode: 409);
        }

        db.RolePermissions.RemoveRange(role.Permissions);
        db.Roles.Remove(role);
        audit.Record(role.TenantId, "role.deleted", "Role", role.Id.ToString(), new { role.Name });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
