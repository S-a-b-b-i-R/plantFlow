using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// A named working period of a plant, in the plant's local time. A shift belongs to the
// service day it starts on, so an evening shift that ends at 02:00 belongs to the day before.
public class PlantShift : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public required string Name { get; set; }

    // Whole minutes. An EndTime at or before StartTime means the shift runs into the next day.
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public ActiveStatus Status { get; set; } = ActiveStatus.Active;

    public bool CrossesMidnight => EndTime <= StartTime;

    public int DurationMinutes
    {
        get
        {
            var (start, end) = Span();
            return end - start;
        }
    }

    // Minutes since midnight. A shift past midnight has an End above 1440.
    public (int Start, int End) Span()
    {
        var start = StartTime.Hour * 60 + StartTime.Minute;
        var end = EndTime.Hour * 60 + EndTime.Minute;
        if (end <= start) end += 24 * 60;
        return (start, end);
    }

    // Shifts repeat every day, so the other shift is also compared one day earlier and one day later.
    // That catches a shift past midnight overlapping one that starts early the next morning.
    public bool OverlapsWith(PlantShift other)
    {
        var a = Span();
        var b = other.Span();

        foreach (var dayOffset in new[] { -24 * 60, 0, 24 * 60 })
        {
            if (a.Start < b.End + dayOffset && b.Start + dayOffset < a.End) return true;
        }

        return false;
    }
}