using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Api.Services;

// Which plants a user works for. A user with no plants assigned is not limited to any plant.
public class PlantScope(PlantFlowDbContext db)
{
    // Null means every plant.
    public async Task<List<Guid>?> PlantsOfAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return [];

        var plants = await db.UserPlants.AsNoTracking().Where(up => up.UserId == userId)
            .Select(up => up.PlantId).ToListAsync(cancellationToken);
        return plants.Count == 0 ? null : plants;
    }

    public async Task<bool> CoversAsync(Guid? userId, Guid plantId, CancellationToken cancellationToken)
    {
        var plants = await PlantsOfAsync(userId, cancellationToken);
        return plants is null || plants.Contains(plantId);
    }

    // The plants a user approves for. Unlike PlantsOfAsync, an approver with no plants approves for none.
    // Admin approves for every plant (null).
    public async Task<List<Guid>?> ApprovesForAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return [];

        var isAdmin = await db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.Role.Name == DefaultRoles.AdminRoleName, cancellationToken);
        if (isAdmin) return null;

        return await db.UserPlants.AsNoTracking().Where(up => up.UserId == userId)
            .Select(up => up.PlantId).ToListAsync(cancellationToken);
    }
}
