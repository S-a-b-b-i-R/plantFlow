using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Controllers;

public record RoleDto(Guid Id, string Name, List<string> Permissions);

[ApiController]
[HasPermission(Permissions.UsersManage)]
public class RolesController(PlantFlowDbContext db) : ControllerBase
{
    [HttpGet("api/roles")]
    public async Task<ActionResult<List<RoleDto>>> ListRoles(CancellationToken cancellationToken)
    {
        return await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList()))
            .ToListAsync(cancellationToken);
    }

    [HttpGet("api/permissions")]
    public ActionResult<IReadOnlyList<string>> ListPermissions() => Ok(Permissions.All);
}