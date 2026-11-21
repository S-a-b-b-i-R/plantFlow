using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// Who may set up plants, shifts and batching plants, and what the plant usage warning reports.
[Collection("api")]
public class PlantSetupTests(ApiFactory factory)
{
    private static object PlantBody(string code, string status = "Active") =>
        new { code, name = $"Plant {code}", timeZone = "UTC", operatingHoursPerDay = 10, status };

    private static object BatchingBody(string code) =>
        new { code, ratedCapacityCftPerHour = 60, batchDurationMinutes = 8 };

    [Fact]
    public async Task A_plant_manager_can_set_up_a_plant_with_shifts_and_batching_plants()
    {
        var company = await factory.CreateCompanyAsync();
        var manager = (await factory.AddMemberAsync(company, "Plant Manager")).Client;

        var plant = await manager.PostAsJsonAsync("/api/plants", PlantBody("PM1"));
        Assert.Equal(HttpStatusCode.Created, plant.StatusCode);
        var plantId = (await plant.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!.Plant.Id;

        var shift = await manager.PostAsJsonAsync($"/api/plants/{plantId}/shifts",
            new { name = "Morning", startTime = "06:00:00", endTime = "14:00:00" });
        var batching = await manager.PostAsJsonAsync($"/api/plants/{plantId}/batching-plants", BatchingBody("BP1"));
        var rename = await manager.PutAsJsonAsync($"/api/plants/{plantId}", PlantBody("PM1-A"));

        Assert.Equal(HttpStatusCode.Created, shift.StatusCode);
        Assert.Equal(HttpStatusCode.Created, batching.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
    }

    [Fact]
    public async Task Master_data_editing_alone_does_not_allow_plant_setup()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var bp = await admin.PostAsJsonAsync($"/api/plants/{plantId}/batching-plants", BatchingBody("BP1"));
        var bpId = (await bp.Content.ReadFromJsonAsync<BatchingPlantDto>(ApiFactory.Json))!.Id;

        // A planner can read plants but not change them.
        var planner = (await factory.AddMemberAsync(company, "Planner")).Client;

        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync($"/api/plants/{plantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.PostAsJsonAsync("/api/plants", PlantBody("P2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.PutAsJsonAsync($"/api/plants/{plantId}", PlantBody("P1", "Inactive"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.PostAsJsonAsync($"/api/plants/{plantId}/batching-plants", BatchingBody("BP2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.PutAsJsonAsync($"/api/batching-plants/{bpId}", BatchingBody("BP1"))).StatusCode);
    }

    [Fact]
    public async Task Usage_counts_open_orders_and_assigned_users()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 50m)]);
        var cancelled = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 20m)]);
        (await admin.PostAsJsonAsync($"/api/orders/{cancelled.Order.Id}/cancel", new { reason = "Customer changed plans" }))
            .EnsureSuccessStatusCode();
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        (await admin.PutAsJsonAsync($"/api/users/{manager.Id}/plants", new { plantIds = new[] { w.PlantId } }))
            .EnsureSuccessStatusCode();

        var usage = await admin.GetFromJsonAsync<PlantUsageDto>($"/api/plants/{w.PlantId}/usage", ApiFactory.Json);

        Assert.Equal(new PlantUsageDto(1, 0, 1), usage);
    }

    [Fact]
    public async Task Usage_needs_master_data_access_and_stays_inside_the_company()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(a.Admin.Client, "P1");
        var dispatcher = (await factory.AddMemberAsync(a, "Dispatcher")).Client;

        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.GetAsync($"/api/plants/{plantId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.Client.GetAsync($"/api/plants/{plantId}/usage")).StatusCode);
    }
}
