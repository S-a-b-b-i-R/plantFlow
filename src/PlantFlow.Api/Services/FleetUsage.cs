using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Services;

// Whether a vehicle can be taken out of use (Maintenance, Retired) or moved to another plant.
// A pump can't while pours are planned for it from today on; a mixer can't while it is out with a load.
public class FleetUsage(PlantFlowDbContext db, PlantUsage plantUsage)
{
    public async Task<string?> RefusalAsync(Vehicle vehicle, string change, CancellationToken cancellationToken)
    {
        if (vehicle.Type == VehicleType.Pump)
        {
            var today = DateOnly.FromDateTime(await plantUsage.LocalNowAsync(vehicle.TenantId, cancellationToken));
            var dates = await db.Pours.AsNoTracking()
                .Where(p => p.PumpId == vehicle.Id && p.ShiftPlan.Date >= today)
                .Select(p => p.ShiftPlan.Date).ToListAsync(cancellationToken);
            if (dates.Count == 0) return null;

            var pours = dates.Count == 1 ? "1 pour is" : $"{dates.Count} pours are";
            return $"{pours} planned for {vehicle.Code} from {dates.Min():d MMM yyyy}. "
                   + $"Move them to another pump before you {change}.";
        }

        var loadOut = await db.TruckLoads.AsNoTracking().AnyAsync(
            l => l.TruckId == vehicle.Id && l.ReturnedAtUtc == null && l.CancelledAtUtc == null, cancellationToken);
        return loadOut ? $"{vehicle.Code} is out with a load. Wait until it is back before you {change}." : null;
    }
}
