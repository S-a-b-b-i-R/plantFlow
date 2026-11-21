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
        // Matched by key, so a built-in role the company renamed is not created again.
        var existing = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.SystemKey != null)
            .Select(r => r.SystemKey!)
            .ToListAsync(cancellationToken);

        foreach (var (name, permissions) in DefaultRoles.All)
        {
            var systemKey = DefaultRoles.KeyFor(name);
            if (existing.Contains(systemKey)) continue;

            var role = new Role { TenantId = tenantId, Name = name, SystemKey = systemKey };
            foreach (var key in permissions)
            {
                role.Permissions.Add(new RolePermission { TenantId = tenantId, PermissionKey = key });
            }

            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}