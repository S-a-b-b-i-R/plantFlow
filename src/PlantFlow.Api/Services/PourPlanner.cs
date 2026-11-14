using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Planning;

namespace PlantFlow.Api.Services;

public record PourTimes(DateTime? PumpLeaveAt, DateTime FirstTruckLeaveAt, int TruckLoads);

// SameShift: the pump is already on another pour in this shift. RestMessage: it has not rested long enough.
public record PumpConflict(bool SameShift, string? RestMessage);

public class PourPlanner(PlantFlowDbContext db)
{
    // The plant-local date and time of a casting start inside a shift, or null if the time is outside the shift.
    // A shift past midnight has its small hours on the next calendar day.
    public static DateTime? CastingStart(PlantShift shift, DateOnly date, TimeOnly time)
    {
        var (start, end) = shift.Span();
        var minutes = time.Hour * 60 + time.Minute;
        if (shift.CrossesMidnight && minutes < start) minutes += 24 * 60;
        if (minutes < start || minutes >= end) return null;

        return date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
    }

    // pumpAssemblyMinutes is null when the pour has no pump.
    public static PourTimes Calculate(
        DateTime start, decimal distanceKm, int speedKmh, decimal plannedCft, decimal truckLoadCft, int? pumpAssemblyMinutes)
    {
        var speed = speedKmh > 0 ? speedKmh : 25;
        var travel = (int)Math.Ceiling(distanceKm * 60m / speed);
        var pumpLeave = pumpAssemblyMinutes is { } assembly ? start.AddMinutes(-(assembly + travel)) : (DateTime?)null;
        var loads = truckLoadCft > 0 ? (int)Math.Ceiling(plannedCft / truckLoadCft) : 0;

        return new PourTimes(pumpLeave, start.AddMinutes(-travel), loads);
    }

    // Shifts are counted per plant in the order they start, continuing across days:
    // Morning, Afternoon, Night, next Morning. A pump needs restShifts shifts between two jobs.
    public async Task<PumpConflict> PumpConflictsAsync(
        ShiftPlan plan, Guid pumpId, Guid? ignorePourId, int restShifts, CancellationToken cancellationToken)
    {
        var shifts = await db.PlantShifts.AsNoTracking().Where(s => s.PlantId == plan.PlantId)
            .OrderBy(s => s.StartTime).Select(s => s.Id).ToListAsync(cancellationToken);

        int Index(DateOnly date, Guid shiftId) => date.DayNumber * shifts.Count + shifts.IndexOf(shiftId);

        var here = Index(plan.Date, plan.ShiftId);
        var from = plan.Date.AddDays(-(restShifts + 1));
        var to = plan.Date.AddDays(restShifts + 1);

        var others = await db.Pours.AsNoTracking()
            .Where(p => p.PumpId == pumpId && p.Id != ignorePourId && p.ShiftPlan.PlantId == plan.PlantId
                        && p.ShiftPlan.Date >= from && p.ShiftPlan.Date <= to)
            .Select(p => new { p.ShiftPlan.Date, p.ShiftPlan.ShiftId, ShiftName = p.ShiftPlan.Shift.Name })
            .ToListAsync(cancellationToken);

        var sameShift = false;
        string? rest = null;

        foreach (var other in others)
        {
            var index = Index(other.Date, other.ShiftId);
            var distance = Math.Abs(index - here);

            if (distance == 0)
            {
                sameShift = true;
            }
            else if (distance <= restShifts)
            {
                rest = index < here
                    ? $"This pump must rest after working the {other.ShiftName} shift on {other.Date:yyyy-MM-dd}."
                    : $"This pump is planned for the {other.ShiftName} shift on {other.Date:yyyy-MM-dd}, too close to this shift for its rest.";
            }
        }

        return new PumpConflict(sameShift, rest);
    }
}