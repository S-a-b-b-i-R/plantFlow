using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;

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
}
