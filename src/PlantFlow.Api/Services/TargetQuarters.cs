namespace PlantFlow.Api.Services;

// Quarters of the company's year. Year is the calendar year the company's year starts in, and
// startMonth is Tenant.YearStartMonth (1 = calendar year, 7 = Bangladesh fiscal year).
public static class TargetQuarters
{
    public static DateOnly Start(int year, int quarter, int startMonth) =>
        new DateOnly(year, startMonth, 1).AddMonths(3 * (quarter - 1));

    // The first day after the quarter.
    public static DateOnly End(int year, int quarter, int startMonth) =>
        Start(year, quarter, startMonth).AddMonths(3);

    public static (int Year, int Quarter) Of(DateOnly date, int startMonth)
    {
        var months = date.Year * 12 + (date.Month - 1) - (startMonth - 1);
        return (months / 12, months % 12 / 3 + 1);
    }

    // "2026 Q1" for a calendar year, "FY 2026–27 Q1" for a year that starts in any other month.
    public static string Label(int year, int quarter, int startMonth) =>
        startMonth == 1 ? $"{year} Q{quarter}" : $"FY {year}–{(year + 1) % 100:00} Q{quarter}";

    public static (int Year, int Quarter) Previous(int year, int quarter) =>
        quarter == 1 ? (year - 1, 4) : (year, quarter - 1);

    public static (int Year, int Quarter) Next(int year, int quarter) =>
        quarter == 4 ? (year + 1, 1) : (year, quarter + 1);
}
