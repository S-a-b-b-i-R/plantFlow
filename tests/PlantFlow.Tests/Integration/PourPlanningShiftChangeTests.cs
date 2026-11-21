using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;

namespace PlantFlow.Tests.Integration;

// A shift with pours planned ahead keeps its times and stays active. It can still be renamed.
public partial class PourPlanningTests
{
    private static string DaysAhead(int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days)).ToString("yyyy-MM-dd");

    private static Task<HttpResponseMessage> PutShiftAsync(
        HttpClient c, Guid shiftId, string name, string start, string end, string status = "Active") =>
        c.PutAsJsonAsync($"/api/plant-shifts/{shiftId}", new { name, startTime = start, endTime = end, status });

    [Fact]
    public async Task A_shift_with_pours_planned_ahead_cannot_change_times_or_be_deactivated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);
        var plan = await PlanAsync(admin, fx.W.PlantId, DaysAhead(30), fx.Morning);
        await ReadPourAsync(await PourAsync(admin, plan.Plan.Id, fx.ChuteLine, 20m, "09:00:00"));

        var retimed = await PutShiftAsync(admin, fx.Morning, "Morning", "07:00:00", "14:00:00");
        var deactivated = await PutShiftAsync(admin, fx.Morning, "Morning", "06:00:00", "14:00:00", "Inactive");
        var renamed = await PutShiftAsync(admin, fx.Morning, "Day", "06:00:00", "14:00:00");

        Assert.Equal(HttpStatusCode.Conflict, retimed.StatusCode);
        Assert.Contains("1 pour is planned in this shift from", await retimed.Content.ReadAsStringAsync());
        Assert.Contains("Move or remove them before you change the shift's times or deactivate it.",
            await retimed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        // The usage warning for the plant counts the same pour.
        var usage = await admin.GetFromJsonAsync<PlantUsageDto>($"/api/plants/{fx.W.PlantId}/usage", ApiFactory.Json);
        Assert.Equal(1, usage!.UpcomingPours);
    }

    [Fact]
    public async Task Past_pours_and_empty_plans_do_not_hold_a_shift()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);
        var past = await PlanAsync(admin, fx.W.PlantId, DaysAhead(-30), fx.Morning);
        await ReadPourAsync(await PourAsync(admin, past.Plan.Id, fx.ChuteLine, 20m, "09:00:00"));
        await PlanAsync(admin, fx.W.PlantId, DaysAhead(30), fx.Afternoon);

        var morning = await PutShiftAsync(admin, fx.Morning, "Morning", "06:00:00", "13:00:00");
        var afternoon = await PutShiftAsync(admin, fx.Afternoon, "Afternoon", "14:00:00", "22:00:00", "Inactive");

        Assert.Equal(HttpStatusCode.NoContent, morning.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afternoon.StatusCode);
    }
}
