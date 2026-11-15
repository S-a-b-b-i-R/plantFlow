using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;

namespace PlantFlow.Tests.Integration;

public partial class PourPlanningTests
{
    private static Task<T?> DashboardAsync<T>(HttpClient c, string url) => c.GetFromJsonAsync<T>(url, ApiFactory.Json);

    [Fact]
    public async Task The_live_board_shows_where_the_loads_are_and_how_late_each_step_is()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var s = await TwoPoursAsync(admin);
        (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).EnsureSuccessStatusCode();

        // One load is pouring on pour A.
        var load = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        var now = DateTime.UtcNow;
        (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        (await Step(admin, load.Id, "Arrived", now.AddMinutes(1))).EnsureSuccessStatusCode();
        (await Step(admin, load.Id, "PouringStarted", now.AddMinutes(2))).EnsureSuccessStatusCode();

        var live = (await DashboardAsync<LiveDashboardDto>(admin, "/api/dashboards/live?date=2026-06-01"))!;
        Assert.Equal(15, live.LateThresholdMinutes);
        Assert.Equal(150m, live.PlannedCft);
        Assert.Equal(40m, live.ProducedCft);

        var plan = Assert.Single(live.Plans);
        Assert.Equal(ShiftPlanStatus.Approved, plan.Status);
        Assert.Equal(110m, plan.RemainingCft);
        Assert.Equal(1, plan.Loads.Pouring);
        Assert.Equal(0, plan.Loads.OnTheRoad);
        Assert.Equal(2, plan.Pours.Count);

        // Pour A: planned for 09:00 plant time, which is 03:00 UTC. It did start, but days after the plan.
        var a = plan.Pours.Single(p => p.PourId == s.PourA);
        Assert.Equal(new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc), a.PourStart!.PlannedUtc);
        Assert.NotNull(a.PourStart.ActualUtc);
        Assert.True(a.PourStart.Late);
        Assert.NotNull(a.FirstTruck!.ActualUtc);
        Assert.True(a.FirstTruck.Late);
        Assert.Equal(1, a.Loads.Pouring);
        Assert.Equal(40m, a.ProducedCft);

        // The pump was never dispatched and is long overdue.
        Assert.Null(a.PumpDispatch!.ActualUtc);
        Assert.True(a.PumpDispatch.Late);
        Assert.True(a.Late);
        // Both pours are late: A's pump never left, and B never started.
        Assert.Equal(2, plan.LatePours);

        // Pour B has no pump, so it has no pump steps. Nothing has been produced for it, and it is overdue too.
        var b = plan.Pours.Single(p => p.PourId == s.PourB);
        Assert.Null(b.PumpDispatch);
        Assert.Null(b.PumpReady);
        Assert.Null(b.PourStart!.ActualUtc);
        Assert.True(b.PourStart.Late);

        // Other dates, other plants and other companies show nothing.
        Assert.Empty((await DashboardAsync<LiveDashboardDto>(admin, "/api/dashboards/live?date=2026-06-02"))!.Plans);
        Assert.Empty((await DashboardAsync<LiveDashboardDto>(admin, $"/api/dashboards/live?date=2026-06-01&plantId={Guid.NewGuid()}"))!.Plans);
        Assert.Empty((await DashboardAsync<LiveDashboardDto>(other.Admin.Client, "/api/dashboards/live?date=2026-06-01"))!.Plans);
    }

    [Fact]
    public async Task The_production_dashboard_adds_up_produced_delivered_returned_and_dumped()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var s = await TwoPoursAsync(admin);
        (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).EnsureSuccessStatusCode();

        // A 40 CFT load is poured. 5 CFT come back, the customer's fault, and are dumped, so they are billed.
        var poured = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        await CompleteLoadAsync(admin, poured.Id);
        var part = (await (await admin.PostAsJsonAsync($"/api/loads/{poured.Id}/part-return",
            new { quantityCft = 5m, fault = "Customer", reason = "Job finished early" }))
            .Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        (await DecideAsync(admin, part.Id, "Dumped", "Set hard")).EnsureSuccessStatusCode();

        // A load for pour B fails QC at the plant and is dumped. It is the company's fault, so it is not billed.
        var bad = await ProduceLoadAsync(admin, s.PourB, s.Truck);
        var rejected = (await (await admin.PostAsJsonAsync($"/api/loads/{bad.Id}/reject", new { reason = "Failed QC" }))
            .Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        (await DecideAsync(admin, rejected.Id, "Dumped", "Beyond treatment")).EnsureSuccessStatusCode();

        const string range = "from=2026-06-01&to=2026-06-01";
        var dash = (await DashboardAsync<ProductionDashboardDto>(admin, $"/api/dashboards/production?{range}"))!;

        Assert.Equal(150m, dash.Total.PlannedCft);
        Assert.Equal(40m, dash.Total.ProducedCft);
        Assert.Equal(40m, dash.Total.DeliveredCft);
        Assert.Equal(45m, dash.Total.ReturnedCft);
        Assert.Equal(45m, dash.Total.DumpedCft);
        Assert.Equal(2, dash.Total.Pours);
        Assert.Equal(2, dash.Total.Loads);
        Assert.Equal(1, dash.Total.RejectedLoads);

        Assert.Equal(dash.Total, Assert.Single(dash.ByPlant).Figures);
        var day = Assert.Single(dash.ByDay);
        Assert.Equal(new DateOnly(2026, 6, 1), day.Date);
        Assert.Equal(dash.Total, day.Figures);
        var product = Assert.Single(dash.ByProduct);
        Assert.False(string.IsNullOrWhiteSpace(product.ProductName));

        // Ranges: the other way round and too long are refused. Elsewhere, in other plants and in other companies, it is empty.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/dashboards/production?from=2026-06-02&to=2026-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/dashboards/production?from=2025-01-01&to=2026-06-01")).StatusCode);
        var none = (await DashboardAsync<ProductionDashboardDto>(admin, "/api/dashboards/production?from=2026-07-01&to=2026-07-31"))!;
        Assert.Equal(0m, none.Total.ProducedCft);
        Assert.Empty(none.ByPlant);
        var wrongPlant = (await DashboardAsync<ProductionDashboardDto>(admin, $"/api/dashboards/production?{range}&plantId={Guid.NewGuid()}"))!;
        Assert.Equal(0m, wrongPlant.Total.PlannedCft);
        var foreign = (await DashboardAsync<ProductionDashboardDto>(other.Admin.Client, $"/api/dashboards/production?{range}"))!;
        Assert.Equal(0m, foreign.Total.PlannedCft);
    }

    [Fact]
    public async Task The_orders_dashboard_counts_orders_by_status_per_plant()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        await TwoPoursAsync(admin);

        var dash = (await DashboardAsync<OrdersDashboardDto>(admin, "/api/dashboards/orders?from=2026-06-01&to=2026-06-30"))!;

        // Both orders were requested for 2026-06-01 and are confirmed. Nothing has been invoiced.
        var confirmed = Assert.Single(dash.ByStatus);
        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        Assert.Equal(3, confirmed.Orders);
        Assert.True(confirmed.ConfirmedValue > 0m);
        Assert.Equal(0, dash.Invoiced.Orders);
        Assert.Equal(0m, dash.Invoiced.Total);

        var plant = Assert.Single(dash.ByPlant);
        Assert.False(string.IsNullOrWhiteSpace(plant.PlantName));
        Assert.Equal(3, Assert.Single(plant.ByStatus).Orders);

        // Outside the range, for another plant and for another company, there is nothing.
        Assert.Empty((await DashboardAsync<OrdersDashboardDto>(admin, "/api/dashboards/orders?from=2026-07-01&to=2026-07-31"))!.ByStatus);
        Assert.Empty((await DashboardAsync<OrdersDashboardDto>(admin,
            $"/api/dashboards/orders?from=2026-06-01&to=2026-06-30&plantId={Guid.NewGuid()}"))!.ByPlant);
        Assert.Empty((await DashboardAsync<OrdersDashboardDto>(other.Admin.Client,
            "/api/dashboards/orders?from=2026-06-01&to=2026-06-30"))!.ByStatus);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/dashboards/orders?from=2026-06-30&to=2026-06-01")).StatusCode);
    }
}