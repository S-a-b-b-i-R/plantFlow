using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Planning;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// What the batching console relies on: the truck list, the return window, the operator role, and rejects.
public partial class PourPlanningTests
{
    private static async Task<ShiftProductionDto> OperatorViewAsync(HttpClient c, Guid planId) =>
        (await c.GetFromJsonAsync<ShiftProductionDto>($"/api/shift-plans/{planId}/operator", ApiFactory.Json))!;

    private static Task<HttpResponseMessage> SetReturnWindowAsync(HttpClient admin, int minutes) =>
        admin.PutAsJsonAsync("/api/settings", new
        {
            name = "Valid Co.",
            volumeUnit = "Cft",
            requireSurveyBeforePlanning = "PumpJobs",
            returnWindowMinutes = minutes
        });

    // Every step at the current time, so a window of zero minutes is already over a moment later.
    private static async Task CompleteLoadNowAsync(HttpClient c, Guid loadId)
    {
        (await c.PostAsJsonAsync($"/api/loads/{loadId}/dispatch", new { })).EnsureSuccessStatusCode();
        foreach (var step in new[] { "Arrived", "PouringStarted", "PouringEnded", "Returned" })
        {
            (await c.PostAsJsonAsync($"/api/loads/{loadId}/steps", new { step })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Only_a_transit_mixer_in_service_can_carry_a_load()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await ApprovedPourAsync(admin);
        var plantId = (await OperatorViewAsync(admin, s.PlanId)).PlantId;
        var loads = $"/api/pours/{s.PourId}/loads";

        var inMaintenance = await CreateMixerAsync(admin, plantId, "MX-MNT", status: "Maintenance");
        var retired = await CreateMixerAsync(admin, plantId, "MX-OLD", status: "Retired");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(loads, new { truckId = s.Pump })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(loads, new { truckId = inMaintenance })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(loads, new { truckId = retired })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(loads, new { truckId = s.Truck })).StatusCode);
    }

    [Fact]
    public async Task The_truck_list_shows_the_plants_available_mixers_with_their_load_size_and_whether_they_are_out()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var s = await ApprovedPourAsync(admin);
        var plantId = (await OperatorViewAsync(admin, s.PlanId)).PlantId;
        var url = $"/api/shift-plans/{s.PlanId}/trucks";

        await CreateMixerAsync(admin, plantId, "MX2", capacityCft: 60m);
        await CreateMixerAsync(admin, plantId, "MX-MNT", status: "Maintenance");

        var list = (await admin.GetFromJsonAsync<List<TruckOptionDto>>(url, ApiFactory.Json))!;
        Assert.Equal(new[] { "MX1", "MX2" }, list.Select(t => t.Code).ToArray());
        Assert.Equal(60m, list.Single(t => t.Code == "MX2").CapacityCft);
        Assert.Equal(100m, list.Single(t => t.Code == "MX1").CapacityCft);
        Assert.All(list, t => Assert.False(t.Busy));

        // A truck with a load on the road is busy until it is back.
        var response = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", new { truckId = s.Truck });
        var load = (await response.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;
        list = (await admin.GetFromJsonAsync<List<TruckOptionDto>>(url, ApiFactory.Json))!;
        Assert.True(list.Single(t => t.Code == "MX1").Busy);
        Assert.False(list.Single(t => t.Code == "MX2").Busy);

        (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/cancel", new { reason = "Changed our mind" })).EnsureSuccessStatusCode();
        list = (await admin.GetFromJsonAsync<List<TruckOptionDto>>(url, ApiFactory.Json))!;
        Assert.All(list, t => Assert.False(t.Busy));

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_part_return_has_to_be_recorded_within_the_window_or_by_a_plant_manager_who_leaves_a_trace()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var planner = await factory.AddMemberAsync(company, "Planner");
        var s = await TwoPoursAsync(admin);
        (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).EnsureSuccessStatusCode();
        var part = new { quantityCft = 5m, fault = "Customer", reason = "Job finished early" };

        var settings = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        Assert.Equal(120, settings.ReturnWindowMinutes);
        Assert.Equal(120, (await OperatorViewAsync(admin, s.PlanId)).ReturnWindowMinutes);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetReturnWindowAsync(admin, 2000)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetReturnWindowAsync(admin, -1)).StatusCode);

        // Inside the window the operator's own permission is enough.
        var early = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        await CompleteLoadNowAsync(admin, early.Id);
        Assert.Equal(HttpStatusCode.Created, (await planner.Client.PostAsJsonAsync($"/api/loads/{early.Id}/part-return", part)).StatusCode);

        // With no window at all, the next load is already too late for the normal route.
        Assert.Equal(HttpStatusCode.NoContent, (await SetReturnWindowAsync(admin, 0)).StatusCode);
        Assert.Equal(0, (await OperatorViewAsync(admin, s.PlanId)).ReturnWindowMinutes);
        var late = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        await CompleteLoadNowAsync(admin, late.Id);
        await Task.Delay(100);

        Assert.Equal(HttpStatusCode.Conflict, (await planner.Client.PostAsJsonAsync($"/api/loads/{late.Id}/part-return", part)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Client.PostAsJsonAsync($"/api/loads/{late.Id}/part-return", part)).StatusCode);

        // Only the manager's override works, and it is audited.
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.PostAsJsonAsync($"/api/loads/{late.Id}/part-return/late", part)).StatusCode);
        var recorded = await manager.Client.PostAsJsonAsync($"/api/loads/{late.Id}/part-return/late", part);
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Client.PostAsJsonAsync($"/api/loads/{late.Id}/part-return/late", part)).StatusCode);

        var audit = (await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit?take=100", ApiFactory.Json))!;
        Assert.Contains(audit, e => e.Action == "load.part_return_late" && e.EntityId == late.Id.ToString());
        Assert.DoesNotContain(audit, e => e.Action == "load.part_return_late" && e.EntityId == early.Id.ToString());
    }

    [Fact]
    public async Task A_pour_without_a_pump_never_waits_for_one()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await TwoPoursAsync(admin);

        // Pump steps on a pour with no pump are refused.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/pours/{s.PourB}/pump-steps", new { step = "Assembled" })).StatusCode);

        var response = await admin.PostAsJsonAsync($"/api/pours/{s.PourB}/loads", new { truckId = s.Truck });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var load = (await response.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;

        (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Step(admin, load.Id, "Arrived")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Step(admin, load.Id, "PouringStarted")).StatusCode);
    }

    [Fact]
    public async Task The_batching_plant_operator_can_run_the_console_but_not_override_the_plan()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var op = (await factory.AddMemberAsync(company, "Batching Plant Operator")).Client;
        var s = await ApprovedPourAsync(admin);
        var loads = $"/api/pours/{s.PourId}/loads";

        // The console reads the live board, the operator view and the truck list.
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/dashboards/live?date=2026-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"/api/shift-plans/{s.PlanId}/operator")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"/api/shift-plans/{s.PlanId}/trucks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"/api/returns?shiftPlanId={s.PlanId}")).StatusCode);

        // It makes loads and records steps.
        var response = await op.PostAsJsonAsync(loads, new { truckId = s.Truck });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var load = (await response.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsJsonAsync($"/api/loads/{load.Id}/dispatch", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Step(op, load.Id, "Arrived")).StatusCode);

        // It cannot go over the plan, and it cannot touch master data.
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync($"{loads}/override",
            new { truckId = s.Truck, overrideReason = "More please" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await op.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 30m })).StatusCode);
    }

    [Fact]
    public async Task A_load_rejected_at_the_plant_shows_up_in_the_stock_list_with_its_product()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await TwoPoursAsync(admin);
        var productId = (await OperatorViewAsync(admin, s.PlanId)).Pours.Single(p => p.PourId == s.PourB).ProductId;

        var load = await ProduceLoadAsync(admin, s.PourB, s.Truck);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/reject", new { reason = "Failed QC" })).StatusCode);

        var stock = (await admin.GetFromJsonAsync<List<ReturnedConcreteDto>>($"/api/returns?shiftPlanId={s.PlanId}", ApiFactory.Json))!;
        var record = Assert.Single(stock);
        Assert.Equal(productId, record.ProductId);
        Assert.False(string.IsNullOrWhiteSpace(record.ProductCode));
        Assert.Equal(ReturnStatus.AwaitingInspection, record.Status);
        Assert.Equal(ReturnFault.Company, record.Fault);
        Assert.Equal(load.QuantityCft, record.QuantityCft);
    }
}
