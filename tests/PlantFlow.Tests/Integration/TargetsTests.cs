using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Targets;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class TargetsTests(ApiFactory factory)
{
    private static async Task<TargetQuarterDto> QuarterAsync(HttpClient c, int? year = null, int? quarter = null) =>
        (await c.GetFromJsonAsync<TargetQuarterDto>(
            year is null ? "/api/targets" : $"/api/targets?year={year}&quarter={quarter}", ApiFactory.Json))!;

    private static Task<HttpResponseMessage> SetAsync(
        HttpClient c, int year, int quarter, string level, decimal volume, Guid? plantId = null, Guid? salesRepId = null) =>
        c.PutAsJsonAsync("/api/targets", new { year, quarter, level, plantId, salesRepId, volumeCft = volume });

    private static async Task<TargetQuarterDto> SetOkAsync(
        HttpClient c, int year, int quarter, string level, decimal volume, Guid? plantId = null, Guid? salesRepId = null)
    {
        var response = await SetAsync(c, year, quarter, level, volume, plantId, salesRepId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TargetQuarterDto>(ApiFactory.Json))!;
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemTitle>())!.Title;

    private sealed record ProblemTitle(string Title);

    private static async Task<Guid> RepAsync(HttpClient admin, string name, Guid? userId = null)
    {
        var response = await admin.PostAsJsonAsync("/api/sales-reps", new { name, userId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SalesRepDto>(ApiFactory.Json))!.Id;
    }

    private static Task<HttpResponseMessage> DeliverAsync(HttpClient c, Guid id, Guid lineId, decimal qty) =>
        c.PutAsJsonAsync($"/api/orders/{id}/deliveries", new { lines = new[] { new { lineId, deliveredCft = qty } } });

    // A confirmed order of 10 CFT, delivered in full and closed.
    private static async Task<Guid> ClosedOrderAsync(HttpClient admin, OrderWorld w)
    {
        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        (await DeliverAsync(admin, order.Order.Id, order.Lines.Single().Id, 10m)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{order.Order.Id}/close", null)).StatusCode);
        return order.Order.Id;
    }

    private async Task SetOrderAsync(Guid orderId, Guid? salesRepId = null, DateTime? closedAtUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId);
        if (salesRepId is not null) order.SalesRepId = salesRepId;
        if (closedAtUtc is not null) order.ClosedAtUtc = closedAtUtc;
        await db.SaveChangesAsync();
    }

    // Plants run on UTC+6 by default.
    private static DateTime StartUtc(TargetQuarterDto q) => q.StartDate.ToDateTime(TimeOnly.MinValue).AddHours(-6);

    [Fact]
    public async Task Quarters_count_from_the_month_the_year_starts()
    {
        Assert.Equal((2026, 2), TargetQuarters.Of(new DateOnly(2026, 11, 21), 7));
        Assert.Equal((2025, 4), TargetQuarters.Of(new DateOnly(2026, 6, 30), 7));
        Assert.Equal((2026, 4), TargetQuarters.Of(new DateOnly(2026, 11, 21), 1));
        Assert.Equal(new DateOnly(2027, 4, 1), TargetQuarters.Start(2026, 4, 7));
        Assert.Equal(new DateOnly(2027, 7, 1), TargetQuarters.End(2026, 4, 7));
        Assert.Equal("FY 2026–27 Q1", TargetQuarters.Label(2026, 1, 7));
        Assert.Equal("FY 2099–00 Q3", TargetQuarters.Label(2099, 3, 7));
        Assert.Equal("2026 Q4", TargetQuarters.Label(2026, 4, 1));

        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var q = await QuarterAsync(admin, 2026, 1);
        Assert.Equal("2026 Q1", q.Label);
        Assert.Equal(new DateOnly(2026, 1, 1), q.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 31), q.EndDate);
        Assert.True(q.Ended);
    }

    [Fact]
    public async Task Targets_show_billed_volume_from_closed_orders_and_provisional_volume_from_open_ones()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await RepAsync(admin, "Rana");

        var closed = await ClosedOrderAsync(admin, w);
        await SetOrderAsync(closed, salesRepId: rep);

        var open = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        (await DeliverAsync(admin, open.Order.Id, open.Lines.Single().Id, 4m)).EnsureSuccessStatusCode();

        var now = await QuarterAsync(admin);
        Assert.True(now.Current);
        Assert.False(now.Ended);

        await SetOkAsync(admin, now.Year, now.Quarter, "Company", 100m);
        await SetOkAsync(admin, now.Year, now.Quarter, "Plant", 60m, plantId: w.PlantId);
        var q = await SetOkAsync(admin, now.Year, now.Quarter, "Rep", 30m, salesRepId: rep);

        Assert.Equal(100m, q.Company!.TargetCft);
        Assert.Equal(10m, q.Company.BilledCft);
        Assert.Equal(4m, q.Company.ProvisionalCft);

        var plant = Assert.Single(q.Plants);
        Assert.Equal(60m, plant.TargetCft);
        Assert.Equal(10m, plant.BilledCft);
        Assert.Equal(4m, plant.ProvisionalCft);

        var rana = Assert.Single(q.Reps);
        Assert.Equal(30m, rana.TargetCft);
        Assert.Equal(10m, rana.BilledCft);
        Assert.Equal(0m, rana.ProvisionalCft);
        Assert.Empty(q.Warnings);

        // Setting it again changes the same target.
        q = await SetOkAsync(admin, now.Year, now.Quarter, "Company", 50m);
        Assert.Equal(50m, q.Company!.TargetCft);

        // Children may add up to more than the company target, with a warning.
        q = await SetOkAsync(admin, now.Year, now.Quarter, "Plant", 80m, plantId: w.PlantId);
        Assert.Equal(["The plant targets add up to 80 CFT, more than the company target of 50 CFT."], q.Warnings);

        var audit = await AuditAsync(admin);
        Assert.Equal(5, audit.Count(a => a.Action == "target.set"));

        // A quarter that hasn't started shows no provisional volume.
        var (ny, nq) = TargetQuarters.Next(now.Year, now.Quarter);
        var next = await QuarterAsync(admin, ny, nq);
        Assert.Equal(0m, next.Company!.ProvisionalCft);
        Assert.Null(next.Company.TargetCft);
    }

    [Fact]
    public async Task The_close_date_in_plant_time_decides_the_quarter()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await ClosedOrderAsync(admin, w);
        var now = await QuarterAsync(admin);
        var (py, pq) = TargetQuarters.Previous(now.Year, now.Quarter);

        // One minute before midnight local time on the quarter's first day: still the previous quarter.
        await SetOrderAsync(order, closedAtUtc: StartUtc(now).AddMinutes(-1));
        Assert.Equal(0m, (await QuarterAsync(admin)).Company!.BilledCft);
        Assert.Equal(10m, (await QuarterAsync(admin, py, pq)).Company!.BilledCft);

        await SetOrderAsync(order, closedAtUtc: StartUtc(now));
        Assert.Equal(10m, (await QuarterAsync(admin)).Company!.BilledCft);
        Assert.Equal(0m, (await QuarterAsync(admin, py, pq)).Company!.BilledCft);
    }

    [Fact]
    public async Task An_ended_quarter_cant_change()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var now = await QuarterAsync(admin);
        var (py, pq) = TargetQuarters.Previous(now.Year, now.Quarter);

        var set = await SetAsync(admin, py, pq, "Company", 100m);
        Assert.Equal(HttpStatusCode.Conflict, set.StatusCode);
        Assert.Equal(TargetsController.EndedMessage, await TitleAsync(set));

        var tenantId = await TenantIdAsync(admin);
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
            var target = new VolumeTarget { TenantId = tenantId, Year = py, Quarter = pq, Level = TargetLevel.Company, VolumeCft = 100m };
            db.VolumeTargets.Add(target);
            await db.SaveChangesAsync();
            id = target.Id;
        }

        var removed = await admin.DeleteAsync($"/api/targets/{id}");
        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        Assert.Equal(TargetsController.EndedMessage, await TitleAsync(removed));
        Assert.Equal(100m, (await QuarterAsync(admin, py, pq)).Company!.TargetCft);
    }

    [Fact]
    public async Task A_target_can_be_removed_while_its_quarter_runs()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var now = await QuarterAsync(admin);
        var q = await SetOkAsync(admin, now.Year, now.Quarter, "Company", 100m);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/targets/{q.Company!.TargetId}")).StatusCode);
        Assert.Null((await QuarterAsync(admin)).Company!.TargetCft);
        Assert.Contains(await AuditAsync(admin), a => a.Action == "target.removed");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/targets/{q.Company.TargetId}")).StatusCode);
    }

    [Fact]
    public async Task Each_refusal_says_what_is_wrong()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var rep = await RepAsync(admin, "Rana");
        var now = await QuarterAsync(admin);
        var (y, q) = (now.Year, now.Quarter);

        async Task Refused(HttpResponseMessage response, string title)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(title, await TitleAsync(response));
        }

        await Refused(await SetAsync(admin, y, q, "Company", 10m, plantId: plantId),
            "A company target is for the whole company, without a plant or sales rep.");
        await Refused(await SetAsync(admin, y, q, "Plant", 10m), "Choose the plant for a plant target.");
        await Refused(await SetAsync(admin, y, q, "Plant", 10m, plantId: plantId, salesRepId: rep),
            "Choose the plant for a plant target.");
        await Refused(await SetAsync(admin, y, q, "Rep", 10m), "Choose the sales rep for a rep target.");
        await Refused(await SetAsync(admin, y, q, "Company", 10.123m), "A target can have at most 2 decimal places.");
        await Refused(await SetAsync(admin, y, q, "Plant", 10m, plantId: Guid.NewGuid()), "That plant was not found.");
        await Refused(await SetAsync(admin, y, q, "Rep", 10m, salesRepId: Guid.NewGuid()), "That sales rep was not found.");
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(admin, y, 5, "Company", 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(admin, y, q, "Company", 0m)).StatusCode);
        await Refused(await admin.GetAsync($"/api/targets?year={y}&quarter=5"),
            "Choose a quarter from 1 to 4 of a year from 2000 to 2100.");
    }

    [Fact]
    public async Task A_sales_rep_sees_only_their_own_target_and_cant_set_targets()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var member = await factory.AddMemberAsync(company, "Sales Representative");
        var mine = await RepAsync(admin, "Rana", member.Id);
        var other = await RepAsync(admin, "Bilal");
        var now = await QuarterAsync(admin);

        await SetOkAsync(admin, now.Year, now.Quarter, "Company", 100m);
        await SetOkAsync(admin, now.Year, now.Quarter, "Plant", 50m, plantId: plantId);
        await SetOkAsync(admin, now.Year, now.Quarter, "Rep", 30m, salesRepId: mine);
        await SetOkAsync(admin, now.Year, now.Quarter, "Rep", 40m, salesRepId: other);

        var seen = await QuarterAsync(member.Client);
        Assert.Null(seen.Company);
        Assert.Empty(seen.Plants);
        Assert.Empty(seen.Warnings);
        var row = Assert.Single(seen.Reps);
        Assert.Equal(mine, row.SalesRepId);
        Assert.Equal(30m, row.TargetCft);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(member.Client, now.Year, now.Quarter, "Rep", 99m, salesRepId: mine)).StatusCode);

        // A manager sees everyone.
        var manager = await factory.AddMemberAsync(company, "Sales Manager");
        Assert.Equal(2, (await QuarterAsync(manager.Client)).Reps.Count);
    }

    [Fact]
    public async Task Targets_need_their_permissions()
    {
        var company = await factory.CreateCompanyAsync();
        var nobody = await factory.AddMemberAsync(company);
        var viewer = await factory.AddMemberAsync(company, "Management");
        var now = await QuarterAsync(company.Admin.Client);
        var target = await SetOkAsync(company.Admin.Client, now.Year, now.Quarter, "Company", 100m);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.Client.GetAsync("/api/targets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync("/api/targets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(viewer.Client, now.Year, now.Quarter, "Company", 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.DeleteAsync($"/api/targets/{target.Company!.TargetId}")).StatusCode);
    }

    [Fact]
    public async Task The_year_start_month_can_change_until_a_target_is_set()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        object Body(int month) => new
        {
            name = "Valid Co.", volumeUnit = "Cft", requireSurveyBeforePlanning = "PumpJobs", pumpRestShifts = 1,
            yearStartMonth = month
        };

        Assert.Equal(1, (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!.YearStartMonth);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/settings", Body(7))).StatusCode);

        var q = await QuarterAsync(admin, 2026, 1);
        Assert.Equal("FY 2026–27 Q1", q.Label);
        Assert.Equal(new DateOnly(2026, 7, 1), q.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), q.EndDate);

        await SetOkAsync(admin, 2099, 1, "Company", 100m);
        var settings = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        Assert.Equal(7, settings.YearStartMonth);
        Assert.True(settings.YearStartLocked);

        var change = await admin.PutAsJsonAsync("/api/settings", Body(1));
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal(SettingsController.YearStartLockedMessage, await TitleAsync(change));
        // Sending the same month again is fine.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/settings", Body(7))).StatusCode);
    }

    private static async Task<Guid> TenantIdAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<MeResponse>("/api/me", ApiFactory.Json))!.TenantId;
}
