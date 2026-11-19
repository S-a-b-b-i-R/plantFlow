using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

public partial class PourPlanningTests
{
    private static Task<List<LineToPlanDto>?> ToPlanAsync(HttpClient c, Guid plantId, string date) =>
        c.GetFromJsonAsync<List<LineToPlanDto>>($"/api/shift-plans/to-plan?plantId={plantId}&date={date}", ApiFactory.Json);

    [Fact]
    public async Task ToPlan_ListsLinesFromTheRequestedDateAndMarksLaterDaysOverdue()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);

        Assert.Empty((await ToPlanAsync(admin, fx.W.PlantId, "2026-05-31"))!);

        var onDay = (await ToPlanAsync(admin, fx.W.PlantId, "2026-06-01"))!;
        Assert.Equal(2, onDay.Count);
        Assert.All(onDay, l => Assert.False(l.Overdue));
        Assert.All(onDay, l => Assert.Equal(l.QuantityCft, l.RemainingCft));

        var later = (await ToPlanAsync(admin, fx.W.PlantId, "2026-06-03"))!;
        Assert.Equal(2, later.Count);
        Assert.All(later, l => Assert.True(l.Overdue));
    }

    [Fact]
    public async Task ToPlan_ShowsWhatIsLeftAndDropsFullyPlannedLines()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);
        var plan = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);

        await ReadPourAsync(await PourAsync(admin, plan.Plan.Id, fx.ChuteLine, 40m, "09:00:00"));
        var partly = (await ToPlanAsync(admin, fx.W.PlantId, "2026-06-01"))!.Single(l => l.OrderLineId == fx.ChuteLine);
        Assert.Equal(40m, partly.PlannedCft);
        Assert.Equal(60m, partly.RemainingCft);

        await ReadPourAsync(await PourAsync(admin, plan.Plan.Id, fx.ChuteLine, 60m, "11:00:00"));
        var left = (await ToPlanAsync(admin, fx.W.PlantId, "2026-06-01"))!;
        Assert.DoesNotContain(left, l => l.OrderLineId == fx.ChuteLine);
        Assert.Contains(left, l => l.OrderLineId == fx.PumpLine);
    }

    [Fact]
    public async Task ToPlan_OnlyShowsThePlantAskedFor()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        await SetupAsync(admin);

        Assert.Empty((await ToPlanAsync(admin, Guid.NewGuid(), "2026-06-01"))!);
    }

    [Fact]
    public async Task ToPlan_NeedsTheTrackingPermission()
    {
        var company = await factory.CreateCompanyAsync();
        var fx = await SetupAsync(company.Admin.Client);
        var credit = await factory.AddMemberAsync(company, "Credit");

        var response = await credit.Client.GetAsync($"/api/shift-plans/to-plan?plantId={fx.W.PlantId}&date=2026-06-01");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
