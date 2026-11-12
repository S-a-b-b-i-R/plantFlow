using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Tests;

public class PlantShiftTests
{
    private static PlantShift Shift(string start, string end) =>
        new() { Name = "Test", StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end) };

    [Theory]
    [InlineData("06:00", "14:00", 480, false)]
    [InlineData("22:00", "06:00", 480, true)]
    [InlineData("18:00", "02:30", 510, true)]
    public void Duration_and_midnight_crossing_are_computed(string start, string end, int minutes, bool crosses)
    {
        var shift = Shift(start, end);

        Assert.Equal(minutes, shift.DurationMinutes);
        Assert.Equal(crosses, shift.CrossesMidnight);
    }

    [Theory]
    [InlineData("06:00", "14:00", "14:00", "22:00", false)] // back to back
    [InlineData("06:00", "14:00", "13:00", "15:00", true)]  // partial overlap
    [InlineData("22:00", "06:00", "04:00", "07:00", true)]  // overlaps the end of a night shift
    [InlineData("22:00", "06:00", "06:00", "14:00", false)] // back to back across midnight
    [InlineData("22:00", "02:00", "23:00", "23:30", true)]  // one inside the other
    [InlineData("22:00", "02:00", "02:00", "05:00", false)] // starts when the other ends
    public void Overlap_is_detected_in_both_directions(string aStart, string aEnd, string bStart, string bEnd, bool expected)
    {
        var a = Shift(aStart, aEnd);
        var b = Shift(bStart, bEnd);

        Assert.Equal(expected, a.OverlapsWith(b));
        Assert.Equal(expected, b.OverlapsWith(a));
    }
}