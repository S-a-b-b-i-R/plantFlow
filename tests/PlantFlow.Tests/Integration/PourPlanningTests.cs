using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class PourPlanningTests(ApiFactory factory)
{
    // One plant with three shifts, two pumps and a confirmed order with a pumped line (500 CFT) and a chute line (100 CFT).
    // The site is surveyed OK unless asked otherwise. The order is 5 km from the plant.
    private sealed record Fixture(
        OrderWorld W, Guid Morning, Guid Afternoon, Guid Night, Guid Pump, Guid Pump2, Guid PumpLine, Guid ChuteLine);

    private static async Task<Fixture> SetupAsync(HttpClient admin, bool withSurvey = true, Guid? pumpDriver = null)
    {
        var w = await CreateOrderWorldAsync(admin);
        var morning = await CreateShiftAsync(admin, w.PlantId, "Morning", "06:00:00", "14:00:00");
        var afternoon = await CreateShiftAsync(admin, w.PlantId, "Afternoon", "14:00:00", "22:00:00");
        var night = await CreateShiftAsync(admin, w.PlantId, "Night", "22:00:00", "06:00:00");
        var pump = await CreatePumpAsync(admin, w.PlantId, "PMP1", pumpDriver);
        var pump2 = await CreatePumpAsync(admin, w.PlantId, "PMP2", assemblyMinutes: 60);

        var order = await CreateConfirmedAsync(admin, w,
        [
            LineBody(w.ProductId, 500m, null, "Slab", "Pump"),
            LineBody(w.ProductId, 100m, null, "Column", "Chute")
        ]);
        if (withSurvey) (await AddSurveyAsync(admin, w.SiteId, "Ok")).EnsureSuccessStatusCode();

        return new Fixture(w, morning, afternoon, night, pump, pump2,
            order.Lines.Single(l => l.QuantityCft == 500m).Id, order.Lines.Single(l => l.QuantityCft == 100m).Id);
    }

    private static async Task<Guid> CreateShiftAsync(HttpClient c, Guid plantId, string name, string start, string end)
    {
        var response = await c.PostAsJsonAsync($"/api/plants/{plantId}/shifts", new { name, startTime = start, endTime = end });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantShiftDto>(ApiFactory.Json))!.Id;
    }

    private static async Task<Guid> CreatePumpAsync(HttpClient c, Guid plantId, string code, Guid? driverId = null, int? assemblyMinutes = null)
    {
        var response = await c.PostAsJsonAsync("/api/vehicles",
            new { plantId, code, type = "Pump", defaultDriverId = driverId, assemblyMinutes });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleDto>(ApiFactory.Json))!.Id;
    }

    private static async Task<Guid> CreateDriverAsync(HttpClient c, string name)
    {
        var response = await c.PostAsJsonAsync("/api/drivers", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DriverDto>(ApiFactory.Json))!.Id;
    }

    private static Task<HttpResponseMessage> AddSurveyAsync(HttpClient c, Guid siteId, string result, string date = "2026-05-30") =>
        c.PostAsJsonAsync($"/api/sites/{siteId}/surveys", new { surveyedOn = date, result });

    private static async Task<ShiftPlanDetailDto> PlanAsync(HttpClient c, Guid plantId, string date, Guid shiftId)
    {
        var response = await c.PostAsJsonAsync("/api/shift-plans", new { plantId, date, shiftId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShiftPlanDetailDto>(ApiFactory.Json))!;
    }

    private static Task<ShiftPlanDetailDto?> PlanDetailAsync(HttpClient c, Guid id) =>
        c.GetFromJsonAsync<ShiftPlanDetailDto>($"/api/shift-plans/{id}", ApiFactory.Json);

    private static Task<HttpResponseMessage> PourAsync(
        HttpClient c, Guid planId, Guid lineId, decimal qty, string start, Guid? pumpId = null,
        Guid? operatorId = null, string? crewNotes = null, string? pumpLeaveAt = null) =>
        c.PostAsJsonAsync($"/api/shift-plans/{planId}/pours",
            new { orderLineId = lineId, plannedCft = qty, startTime = start, pumpId, operatorId, crewNotes, pumpLeaveAt });

    private static Task<HttpResponseMessage> OverrideAsync(
        HttpClient c, Guid planId, Guid lineId, decimal qty, string start, Guid pumpId, string reason = "Urgent pour, no other pump") =>
        c.PostAsJsonAsync($"/api/shift-plans/{planId}/pours/override",
            new { reason, pour = new { orderLineId = lineId, plannedCft = qty, startTime = start, pumpId } });

    private static async Task<PourDto> ReadPourAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PourDto>(ApiFactory.Json))!;
    }

    private static async Task SetSettingsAsync(HttpClient admin, string survey = "PumpJobs", int rest = 1)
    {
        var s = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        (await admin.PutAsJsonAsync("/api/settings", new
        {
            s.Name,
            s.VolumeUnit,
            s.PumpFreeAboveCft,
            requireSurveyBeforePlanning = survey,
            pumpRestShifts = rest,
            s.RequireCreditApproval,
            s.VatOnConcrete,
            s.AverageSpeedKmh,
            s.PumpAssemblyMinutes,
            s.TruckLoadCft
        }, ApiFactory.Json)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Casting_time_gives_the_pump_and_first_truck_times_and_the_truck_estimate()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);
        var morning = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);
        var night = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Night);

        // 5 km at 25 km/h is 12 minutes. The first pump assembles in 90 minutes (company default).
        var a = await ReadPourAsync(await PourAsync(admin, morning.Plan.Id, fx.PumpLine, 250m, "09:00:00", fx.Pump));
        Assert.Equal(new DateTime(2026, 6, 1, 9, 0, 0), a.PlannedStart);
        Assert.Equal(new DateTime(2026, 6, 1, 7, 18, 0), a.PumpLeaveAt);
        Assert.Equal(new DateTime(2026, 6, 1, 8, 48, 0), a.FirstTruckLeaveAt);
        Assert.Equal(3, a.EstimatedTruckLoads);   // 250 CFT at 100 CFT per truck

        // The second pump assembles in 60 minutes.
        var b = await ReadPourAsync(await PourAsync(admin, morning.Plan.Id, fx.PumpLine, 50m, "10:30:00", fx.Pump2));
        Assert.Equal(new DateTime(2026, 6, 1, 9, 18, 0), b.PumpLeaveAt);
        Assert.Equal(1, b.EstimatedTruckLoads);

        // A night shift runs past midnight: 01:00 belongs to the next calendar day. The planner overrides the pump time.
        var c = await ReadPourAsync(await PourAsync(
            admin, night.Plan.Id, fx.PumpLine, 40m, "01:00:00", fx.Pump2, pumpLeaveAt: "2026-06-01T23:00:00"));
        Assert.Equal(new DateTime(2026, 6, 2, 1, 0, 0), c.PlannedStart);
        Assert.Equal(new DateTime(2026, 6, 1, 23, 0, 0), c.PumpLeaveAt);
        Assert.Equal(new DateTime(2026, 6, 2, 0, 48, 0), c.FirstTruckLeaveAt);

        // A leave time after the casting start makes no sense.
        var late = await PourAsync(admin, morning.Plan.Id, fx.ChuteLine, 10m, "09:00:00", pumpLeaveAt: "2026-06-01T09:30:00");
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task Pours_follow_the_order_the_survey_and_the_shift()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin, withSurvey: false);
        var plan = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);
        var id = plan.Plan.Id;

        // Pump jobs need a survey with an OK result. Other pours do not under the PumpJobs setting.
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(admin, id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, id, fx.ChuteLine, 20m, "09:00:00")).StatusCode);

        (await AddSurveyAsync(admin, fx.W.SiteId, "NotOk", "2026-05-30")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(admin, id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);

        (await AddSurveyAsync(admin, fx.W.SiteId, "Ok", "2026-05-31")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, id, fx.PumpLine, 300m, "09:00:00", fx.Pump)).StatusCode);

        // The planned total cannot pass the ordered 500 CFT.
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(admin, id, fx.PumpLine, 250m, "10:00:00", fx.Pump2)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, id, fx.PumpLine, 200m, "10:00:00", fx.Pump2)).StatusCode);

        // A pumped line needs a pump, another line cannot have one, and the time must be inside the shift.
        Assert.Equal(HttpStatusCode.BadRequest, (await PourAsync(admin, id, fx.PumpLine, 1m, "09:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PourAsync(admin, id, fx.ChuteLine, 1m, "09:00:00", fx.Pump2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PourAsync(admin, id, fx.ChuteLine, 1m, "05:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PourAsync(admin, id, fx.ChuteLine, 1m, "14:00:00")).StatusCode);

        // A draft order cannot be planned yet.
        var draft = await CreateDraftAsync(admin, fx.W, [LineBody(fx.W.ProductId, 10m, null, "Slab", "Chute")]);
        Assert.Equal(HttpStatusCode.BadRequest, (await PourAsync(admin, id, draft.Lines.Single().Id, 5m, "09:00:00")).StatusCode);

        // With the survey requirement off, no survey is needed.
        var other = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx2 = await SetupAsync(other, withSurvey: false);
        await SetSettingsAsync(other, survey: "Off");
        var plan2 = await PlanAsync(other, fx2.W.PlantId, "2026-06-01", fx2.Morning);
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(other, plan2.Plan.Id, fx2.PumpLine, 10m, "09:00:00", fx2.Pump)).StatusCode);
    }

    [Fact]
    public async Task A_pump_must_rest_after_a_shift_and_a_plant_manager_can_override()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var planner = await factory.AddMemberAsync(company, "Planner");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var fx = await SetupAsync(admin);

        var m1 = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);
        var a1 = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Afternoon);
        var n1 = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Night);
        var m2 = await PlanAsync(admin, fx.W.PlantId, "2026-06-02", fx.Morning);

        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, m1.Plan.Id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);

        // The afternoon right after a morning shift is blocked.
        var afternoon = await PourAsync(admin, a1.Plan.Id, fx.PumpLine, 10m, "15:00:00", fx.Pump);
        Assert.Equal(HttpStatusCode.Conflict, afternoon.StatusCode);
        Assert.Contains("must rest", await afternoon.Content.ReadAsStringAsync());

        // The night shift is two shifts later, so it is fine. The next morning follows the night shift and is blocked.
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, n1.Plan.Id, fx.PumpLine, 10m, "23:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(admin, m2.Plan.Id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);

        // Another pump in the same shift is fine. The same pump twice in a shift is never allowed, even with an override.
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, m1.Plan.Id, fx.PumpLine, 10m, "10:00:00", fx.Pump2)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(admin, m1.Plan.Id, fx.PumpLine, 10m, "11:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OverrideAsync(manager.Client, m1.Plan.Id, fx.PumpLine, 10m, "11:00:00", fx.Pump)).StatusCode);

        // The override needs its own permission, which a planner lacks. It also needs a reason.
        Assert.Equal(HttpStatusCode.Forbidden, (await OverrideAsync(planner.Client, a1.Plan.Id, fx.PumpLine, 10m, "15:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await OverrideAsync(manager.Client, a1.Plan.Id, fx.PumpLine, 10m, "15:00:00", fx.Pump, reason: "")).StatusCode);

        var forced = await ReadPourAsync(await OverrideAsync(manager.Client, a1.Plan.Id, fx.PumpLine, 10m, "15:00:00", fx.Pump));
        Assert.Equal("Urgent pour, no other pump", forced.RestOverrideReason);

        // With the rest rule off, only the same shift is blocked.
        await SetSettingsAsync(admin, rest: 0);
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(admin, m2.Plan.Id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);
    }

    [Fact]
    public async Task A_shift_plan_goes_through_the_plant_head()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var planner = await factory.AddMemberAsync(company, "Planner");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var fx = await SetupAsync(admin);

        var plan = await PlanAsync(planner.Client, fx.W.PlantId, "2026-06-01", fx.Morning);
        var id = plan.Plan.Id;
        var duplicate = await planner.Client.PostAsJsonAsync("/api/shift-plans",
            new { plantId = fx.W.PlantId, date = "2026-06-01", shiftId = fx.Morning });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await planner.Client.PostAsync($"/api/shift-plans/{id}/submit", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await PourAsync(planner.Client, id, fx.PumpLine, 10m, "09:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await planner.Client.PostAsync($"/api/shift-plans/{id}/submit", null)).StatusCode);
        Assert.Equal(ShiftPlanStatus.Submitted, (await PlanDetailAsync(admin, id))!.Plan.Status);

        // A submitted plan is locked for planning, and only the plant head can approve it.
        Assert.Equal(HttpStatusCode.Conflict, (await PourAsync(planner.Client, id, fx.PumpLine, 10m, "10:00:00", fx.Pump2)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.PostAsync($"/api/shift-plans/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.Client.PostAsync($"/api/shift-plans/{id}/approve", null)).StatusCode);

        var approved = (await PlanDetailAsync(admin, id))!;
        Assert.Equal(ShiftPlanStatus.Approved, approved.Plan.Status);
        Assert.NotNull(approved.Plan.ApprovedAtUtc);
        Assert.Equal(HttpStatusCode.Conflict, (await planner.Client.PostAsync($"/api/shift-plans/{id}/submit", null)).StatusCode);

        // The plant head reopens it with a reason, the planners fix it, and it is submitted again.
        var noReason = await manager.Client.PostAsJsonAsync($"/api/shift-plans/{id}/reopen", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var reopen = await manager.Client.PostAsJsonAsync($"/api/shift-plans/{id}/reopen", new { reason = "Customer moved the pour" });
        Assert.Equal(HttpStatusCode.NoContent, reopen.StatusCode);

        var draft = (await PlanDetailAsync(admin, id))!;
        Assert.Equal(ShiftPlanStatus.Draft, draft.Plan.Status);
        Assert.Equal("Customer moved the pour", draft.Plan.ReturnReason);
        Assert.Equal(HttpStatusCode.Created, (await PourAsync(planner.Client, id, fx.PumpLine, 10m, "10:00:00", fx.Pump2)).StatusCode);

        (await planner.Client.PostAsync($"/api/shift-plans/{id}/submit", null)).EnsureSuccessStatusCode();
        var noRejectReason = await manager.Client.PostAsJsonAsync($"/api/shift-plans/{id}/reject", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noRejectReason.StatusCode);
        var reject = await manager.Client.PostAsJsonAsync($"/api/shift-plans/{id}/reject", new { reason = "Wrong pump for this site" });
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var back = (await PlanDetailAsync(admin, id))!;
        Assert.Equal(ShiftPlanStatus.Draft, back.Plan.Status);
        Assert.Equal("Wrong pump for this site", back.Plan.ReturnReason);

        // The list shows the totals, and a pour can be removed from a draft.
        var list = await admin.GetFromJsonAsync<PagedResult<ShiftPlanSummaryDto>>("/api/shift-plans?status=Draft", ApiFactory.Json);
        var row = Assert.Single(list!.Items);
        Assert.Equal(2, row.PourCount);
        Assert.Equal(20m, row.TotalPlannedCft);

        var pourId = back.Pours.First().Id;
        Assert.Equal(HttpStatusCode.NoContent, (await planner.Client.DeleteAsync($"/api/pours/{pourId}")).StatusCode);
        Assert.Single((await PlanDetailAsync(admin, id))!.Pours);
    }

    [Fact]
    public async Task A_pump_has_a_crew_and_an_operator_and_other_companies_cannot_see_the_plan()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var rahim = await CreateDriverAsync(admin, "Rahim");
        var karim = await CreateDriverAsync(admin, "Karim");
        var fx = await SetupAsync(admin, pumpDriver: rahim);

        // The named labour team.
        var put = await admin.PutAsJsonAsync($"/api/vehicles/{fx.Pump}/crew", new { names = new[] { "Jewel", "Hasan" } });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(["Hasan", "Jewel"], await admin.GetFromJsonAsync<List<string>>($"/api/vehicles/{fx.Pump}/crew", ApiFactory.Json));

        var duplicate = await admin.PutAsJsonAsync($"/api/vehicles/{fx.Pump}/crew", new { names = new[] { "Hasan", "hasan" } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var mixer = await admin.PostAsJsonAsync("/api/vehicles",
            new { plantId = fx.W.PlantId, code = "TM1", type = "TransitMixer", capacityCft = 8m });
        var mixerId = (await mixer.Content.ReadFromJsonAsync<VehicleDto>(ApiFactory.Json))!.Id;
        var notAPump = await admin.PutAsJsonAsync($"/api/vehicles/{mixerId}/crew", new { names = new[] { "Hasan" } });
        Assert.Equal(HttpStatusCode.BadRequest, notAPump.StatusCode);

        var plan = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);

        // The operator is the pump's default driver, and the crew is the pump's team.
        var first = await ReadPourAsync(await PourAsync(admin, plan.Plan.Id, fx.PumpLine, 10m, "09:00:00", fx.Pump));
        Assert.Equal("Rahim", first.OperatorName);
        Assert.Equal(["Hasan", "Jewel"], first.Crew);

        // A pour can use another operator and add notes about its crew.
        var second = await ReadPourAsync(await PourAsync(
            admin, plan.Plan.Id, fx.PumpLine, 10m, "10:00:00", fx.Pump2, operatorId: karim, crewNotes: "Four labourers from team B"));
        Assert.Equal("Karim", second.OperatorName);
        Assert.Empty(second.Crew);
        Assert.Equal("Four labourers from team B", second.CrewNotes);

        var detail = (await PlanDetailAsync(admin, plan.Plan.Id))!;
        Assert.Equal(2, detail.Pours.Count);
        Assert.Equal("PMP1", detail.Pours[0].PumpCode);

        // Another company sees nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/shift-plans/{plan.Plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PourAsync(other.Admin.Client, plan.Plan.Id, fx.PumpLine, 5m, "09:00:00", fx.Pump)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/vehicles/{fx.Pump}/crew")).StatusCode);
    }
}