using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// The getting-started list a new company's Admin sees, and its progress on the platform console.
[Collection("api")]
public class SetupChecklistTests(ApiFactory factory)
{
    private static async Task<SetupProgressDto> ProgressAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<SetupProgressDto>("/api/setup", ApiFactory.Json))!;

    private static async Task<HashSet<string>> DoneAsync(HttpClient admin) =>
        (await ProgressAsync(admin)).Steps.Where(s => s.Done).Select(s => s.Key).ToHashSet();

    [Fact]
    public async Task A_new_company_starts_with_nothing_done_and_each_step_ticks_off_as_it_is_set_up()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;

        var start = await ProgressAsync(admin);
        Assert.Equal(10, start.Total);
        Assert.Equal(0, start.Done);
        Assert.Equal(
            ["settings", "plant", "fleet", "catalogue", "recipe", "costs", "prices", "sales-rep", "customer", "people"],
            start.Steps.Select(s => s.Key));

        // Settings: saved at least once.
        var settings = (await admin.GetFromJsonAsync<JsonObject>("/api/settings", ApiFactory.Json))!;
        settings["reason"] = "Setting up";
        (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        Assert.Contains("settings", await DoneAsync(admin));

        // Plant: needs both a shift and a batching plant.
        var plantId = await CreatePlantAsync(admin, "SC1");
        (await admin.PostAsJsonAsync($"/api/plants/{plantId}/shifts",
            new { name = "Morning", startTime = "06:00:00", endTime = "14:00:00" })).EnsureSuccessStatusCode();
        Assert.DoesNotContain("plant", await DoneAsync(admin));
        (await admin.PostAsJsonAsync($"/api/plants/{plantId}/batching-plants",
            new { code = "BP1", ratedCapacityCftPerHour = 60, batchDurationMinutes = 8 })).EnsureSuccessStatusCode();
        Assert.Contains("plant", await DoneAsync(admin));

        // Fleet: a transit mixer and a driver; a pump alone doesn't count.
        (await admin.PostAsJsonAsync("/api/vehicles", new { plantId, code = "PU-1", type = "Pump" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/drivers", new { name = "Rahim" })).EnsureSuccessStatusCode();
        Assert.DoesNotContain("fleet", await DoneAsync(admin));
        (await admin.PostAsJsonAsync("/api/vehicles",
            new { plantId, code = "TM-1", type = "TransitMixer", capacityCft = 300 })).EnsureSuccessStatusCode();
        Assert.Contains("fleet", await DoneAsync(admin));

        // Catalogue, recipe, costs, prices and a customer with a site all come with an order-ready world.
        await CreateOrderWorldAsync(admin);
        var done = await DoneAsync(admin);
        Assert.Superset(new HashSet<string> { "catalogue", "recipe", "costs", "prices", "customer" }, done);
        Assert.DoesNotContain("sales-rep", done);
        Assert.DoesNotContain("people", done);

        // A second person, linked as a sales rep, finishes it.
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        (await admin.PostAsJsonAsync("/api/sales-reps", new { name = "Karim", userId = rep.Id })).EnsureSuccessStatusCode();
        var end = await ProgressAsync(admin);
        Assert.Equal(end.Total, end.Done);

        // The platform console shows the same progress, per company.
        var tenants = await factory.PlatformAdmin()
            .GetFromJsonAsync<List<TenantSummaryDto>>("/api/platform/tenants", ApiFactory.Json);
        var row = tenants!.Single(t => t.Id == company.TenantId);
        Assert.Equal((10, 10), (row.SetupDone, row.SetupTotal));
    }

    [Fact]
    public async Task Another_company_s_setup_never_counts()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        await CreateOrderWorldAsync(other.Admin.Client);

        Assert.Equal(0, (await ProgressAsync(company.Admin.Client)).Done);
        var tenants = await factory.PlatformAdmin()
            .GetFromJsonAsync<List<TenantSummaryDto>>("/api/platform/tenants", ApiFactory.Json);
        Assert.Equal(0, tenants!.Single(t => t.Id == company.TenantId).SetupDone);
        Assert.True(tenants!.Single(t => t.Id == other.TenantId).SetupDone >= 5);
    }

    [Fact]
    public async Task Only_admins_see_the_checklist()
    {
        var company = await factory.CreateCompanyAsync();
        var planner = await factory.AddMemberAsync(company, "Planner");
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/setup")).StatusCode);
    }
}
