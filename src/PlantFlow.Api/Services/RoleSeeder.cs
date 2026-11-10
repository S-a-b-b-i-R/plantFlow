using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Api.Services;

public class RoleSeeder(PlantFlowDbContext db)
{
    // Safe to call repeatedly: only roles that are missing get created.
    public async Task SeedDefaultRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var existing = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        foreach (var (name, permissions) in DefaultRoles.All)
        {
            if (existing.Contains(name)) continue;

            var role = new Role { TenantId = tenantId, Name = name };
            foreach (var key in permissions)
            {
                role.Permissions.Add(new RolePermission { TenantId = tenantId, PermissionKey = key });
            }

            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}