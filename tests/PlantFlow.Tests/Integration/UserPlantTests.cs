using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class UserPlantTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> SetPlantsAsync(HttpClient client, Guid userId, params Guid[] plantIds) =>
        client.PutAsJsonAsync($"/api/users/{userId}/plants", new { plantIds });

    private static async Task<Guid> CreateInactivePlantAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/plants",
            new { code, name = $"Plant {code}", timeZone = "Asia/Dhaka", operatingHoursPerDay = 10, status = "Inactive" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!.Plant.Id;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_admin_assigns_plants_and_they_show_on_the_user()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var north = await TestData.CreatePlantAsync(admin, "N1");
        var south = await TestData.CreatePlantAsync(admin, "S1");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");

        (await SetPlantsAsync(admin, manager.Id, north, south)).EnsureSuccessStatusCode();

        var detail = await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{manager.Id}", ApiFactory.Json);
        Assert.Equal([north, south], detail!.PlantIds.Order());

        var listed = (await admin.GetFromJsonAsync<List<UserDto>>("/api/users", ApiFactory.Json))!
            .Single(u => u.Id == manager.Id);
        Assert.Equal(["N1", "S1"], listed.Plants.Select(p => p.Code));
    }

    [Fact]
    public async Task Setting_plants_replaces_the_previous_set_and_an_empty_list_clears_it()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var north = await TestData.CreatePlantAsync(admin, "N1");
        var south = await TestData.CreatePlantAsync(admin, "S1");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");

        (await SetPlantsAsync(admin, manager.Id, north)).EnsureSuccessStatusCode();
        (await SetPlantsAsync(admin, manager.Id, south, south)).EnsureSuccessStatusCode();

        var detail = await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{manager.Id}", ApiFactory.Json);
        Assert.Equal([south], detail!.PlantIds);

        (await SetPlantsAsync(admin, manager.Id)).EnsureSuccessStatusCode();
        detail = await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{manager.Id}", ApiFactory.Json);
        Assert.Empty(detail!.PlantIds);
    }

    [Fact]
    public async Task An_unknown_plant_is_refused()
    {
        var company = await factory.CreateCompanyAsync();
        var manager = await factory.AddMemberAsync(company, "Plant Manager");

        await AssertProblemAsync(
            await SetPlantsAsync(company.Admin.Client, manager.Id, Guid.CreateVersion7()),
            HttpStatusCode.BadRequest, "One or more plants do not exist in this company.");
    }

    [Fact]
    public async Task An_inactive_plant_cannot_be_newly_assigned_but_an_existing_one_can_stay()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var closed = await CreateInactivePlantAsync(admin, "OLD");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");

        await AssertProblemAsync(
            await SetPlantsAsync(admin, manager.Id, closed),
            HttpStatusCode.BadRequest, "An inactive plant cannot be assigned. Reactivate the plant first.");

        // Assigned while active, then deactivated: saving the same set again is fine.
        var plant = await TestData.CreatePlantAsync(admin, "P1");
        (await SetPlantsAsync(admin, manager.Id, plant)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/plants/{plant}",
            new { code = "P1", name = "Plant P1", timeZone = "Asia/Dhaka", operatingHoursPerDay = 10, status = "Inactive" }))
            .EnsureSuccessStatusCode();

        (await SetPlantsAsync(admin, manager.Id, plant)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_plants()
    {
        var company = await factory.CreateCompanyAsync();
        var plant = await TestData.CreatePlantAsync(company.Admin.Client, "P1");

        await AssertProblemAsync(
            await SetPlantsAsync(company.Admin.Client, company.Admin.Id, plant),
            HttpStatusCode.Conflict, "You cannot change your own account.");
    }

    [Fact]
    public async Task Only_users_with_users_manage_can_assign_plants()
    {
        var company = await factory.CreateCompanyAsync();
        var plant = await TestData.CreatePlantAsync(company.Admin.Client, "P1");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var other = await factory.AddMemberAsync(company, "Planner");

        var response = await SetPlantsAsync(manager.Client, other.Id, plant);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_plants_is_audited()
    {
        var company = await factory.CreateCompanyAsync();
        var plant = await TestData.CreatePlantAsync(company.Admin.Client, "P1");
        var manager = await factory.AddMemberAsync(company, "Plant Manager");

        (await SetPlantsAsync(company.Admin.Client, manager.Id, plant)).EnsureSuccessStatusCode();

        var audit = await TestData.AuditAsync(company.Admin.Client);
        Assert.Contains(audit!, e => e.Action == "user.plants_changed" && e.EntityId == manager.Id.ToString());
    }
}
