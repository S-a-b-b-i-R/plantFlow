using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Services;

// What still relies on a plant or a shift. Deactivating a plant only warns; changing a shift that
// has pours planned ahead is refused, because those pours were timed against the shift.
public class PlantUsage(PlantFlowDbContext db)
{
    public static readonly OrderStatus[] ClosedStatuses =
        [OrderStatus.Completed, OrderStatus.Rejected, OrderStatus.Cancelled];

    public record Summary(int OpenOrders, int UpcomingPours, int AssignedUsers);

    public record UpcomingPours(int Count, DateOnly FirstDate);

    // The company's local time. Plans and shifts are kept in plant-local time with one company-wide offset.
    public async Task<DateTime> LocalNowAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var offset = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => t.UtcOffsetMinutes).FirstAsync(cancellationToken);
        return DateTime.UtcNow.AddMinutes(offset);
    }

    public async Task<Summary> OfPlantAsync(Guid tenantId, Guid plantId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(await LocalNowAsync(tenantId, cancellationToken));

        var openOrders = await db.Orders.AsNoTracking()
            .CountAsync(o => o.PlantId == plantId && !ClosedStatuses.Contains(o.Status), cancellationToken);
        var upcomingPours = await db.Pours.AsNoTracking()
            .CountAsync(p => p.ShiftPlan.PlantId == plantId && p.ShiftPlan.Date >= today, cancellationToken);
        var assignedUsers = await db.UserPlants.AsNoTracking()
            .CountAsync(up => up.PlantId == plantId, cancellationToken);

        return new Summary(openOrders, upcomingPours, assignedUsers);
    }

    // Pours on plans of this shift that have not finished yet. A shift past midnight that started
    // yesterday is still running, so yesterday's plan counts until the shift ends.
    public async Task<UpcomingPours?> OfShiftAsync(PlantShift shift, CancellationToken cancellationToken)
    {
        var now = await LocalNowAsync(shift.TenantId, cancellationToken);
        var yesterday = DateOnly.FromDateTime(now).AddDays(-1);

        var plans = await db.ShiftPlans.AsNoTracking()
            .Where(p => p.ShiftId == shift.Id && p.Date >= yesterday && p.Pours.Any())
            .Select(p => new { p.Date, Pours = p.Pours.Count })
            .ToListAsync(cancellationToken);

        var open = plans.Where(p => NotFinished(shift, p.Date, now)).ToList();

        return open.Count == 0 ? null : new UpcomingPours(open.Sum(p => p.Pours), open.Min(p => p.Date));
    }

    // True while the shift on that service day has not ended, in local time.
    public static bool NotFinished(PlantShift shift, DateOnly date, DateTime localNow) =>
        date.ToDateTime(TimeOnly.MinValue).AddMinutes(shift.Span().End) > localNow;
}
