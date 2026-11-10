using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class PlantTests(ApiFactory factory)
{
    private static object NewPlant(string code, string name = "Test Plant") =>
        new { code, name, timeZone = "Asia/Dhaka", operatingHoursPerDay = 10 };

    private static async Task<PlantDetailDto> CreatePlantAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/plants", NewPlant(code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!;
    }

    private static async Task<HttpResponseMessage> AddBatchingPlantAsync(
        HttpClient client, Guid plantId, string code, decimal capacity, string status = "Active") =>
        await client.PostAsJsonAsync($"/api/plants/{plantId}/batching-plants",
            new { code, make = "Acme", model = "X1", ratedCapacityCftPerHour = capacity, batchDurationMinutes = 8, status });

    [Fact]
    public async Task Daily_capacity_is_derived_from_active_batching_plants()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var plant = await CreatePlantAsync(admin, "dhk-1");
        Assert.Equal("DHK-1", plant.Plant.Code); // codes are stored in upper case

        await AddBatchingPlantAsync(admin, plant.Plant.Id, "bp1", 60);
        var second = await AddBatchingPlantAsync(admin, plant.Plant.Id, "bp2", 40);
        var secondDto = (await second.Content.ReadFromJsonAsync<BatchingPlantDto>(ApiFactory.Json))!;

        var detail = await admin.GetFromJsonAsync<PlantDetailDto>($"/api/plants/{plant.Plant.Id}", ApiFactory.Json);
        Assert.Equal(2, detail!.BatchingPlants.Count);
        Assert.Equal(1000m, detail.Plant.DailyCapacityCft); // (60 + 40) per hour x 10 hours

        // Deactivating a batching plant removes its capacity.
        var update = await admin.PutAsJsonAsync($"/api/batching-plants/{secondDto.Id}", new
        {
            code = "BP2", ratedCapacityCftPerHour = 40, batchDurationMinutes = 8, status = "Inactive"
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        detail = await admin.GetFromJsonAsync<PlantDetailDto>($"/api/plants/{plant.Plant.Id}", ApiFactory.Json);
        Assert.Equal(600m, detail!.Plant.DailyCapacityCft);
    }

    [Fact]
    public async Task Codes_must_be_unique_within_a_company_but_not_across_companies()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();

        await CreatePlantAsync(a.Admin.Client, "MAIN");

        var duplicate = await a.Admin.Client.PostAsJsonAsync("/api/plants", NewPlant("main"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var sameCodeElsewhere = await b.Admin.Client.PostAsJsonAsync("/api/plants", NewPlant("MAIN"));
        Assert.Equal(HttpStatusCode.Created, sameCodeElsewhere.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;

        var badZone = await admin.PostAsJsonAsync("/api/plants",
            new { code = "Z1", name = "Zone Test", timeZone = "Mars/Olympus", operatingHoursPerDay = 10 });
        var badCode = await admin.PostAsJsonAsync("/api/plants", NewPlant("has space"));
        var badHours = await admin.PostAsJsonAsync("/api/plants",
            new { code = "H1", name = "Hours Test", timeZone = "UTC", operatingHoursPerDay = 30 });

        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badHours.StatusCode);
    }

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var plant = await CreatePlantAsync(company.Admin.Client, "VIEW1");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var list = await rep.Client.GetAsync("/api/plants");
        var create = await rep.Client.PostAsJsonAsync("/api/plants", NewPlant("REP1"));
        var update = await rep.Client.PutAsJsonAsync($"/api/plants/{plant.Plant.Id}", NewPlant("VIEW1"));
        var addBatchingPlant = await AddBatchingPlantAsync(rep.Client, plant.Plant.Id, "BP1", 50);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addBatchingPlant.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_a_plant()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var plant = await CreatePlantAsync(a.Admin.Client, "PRIVATE");

        var listSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<PlantSummaryDto>>(
            "/api/plants", ApiFactory.Json);
        var get = await b.Admin.Client.GetAsync($"/api/plants/{plant.Plant.Id}");
        var put = await b.Admin.Client.PutAsJsonAsync($"/api/plants/{plant.Plant.Id}", NewPlant("HACKED"));
        var addBatchingPlant = await AddBatchingPlantAsync(b.Admin.Client, plant.Plant.Id, "BP1", 50);

        Assert.Empty(listSeenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, addBatchingPlant.StatusCode);
    }

    [Fact]
    public async Task The_list_supports_paging_search_and_sorting()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        await admin.PostAsJsonAsync("/api/plants", NewPlant("A1", "Alpha"));
        await admin.PostAsJsonAsync("/api/plants", NewPlant("B1", "Bravo"));
        await admin.PostAsJsonAsync("/api/plants", NewPlant("C1", "Charlie"));

        var firstPage = await admin.GetFromJsonAsync<PagedResult<PlantSummaryDto>>(
            "/api/plants?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<PlantSummaryDto>>(
            "/api/plants?pageSize=2&page=2", ApiFactory.Json);
        var found = await admin.GetFromJsonAsync<PagedResult<PlantSummaryDto>>(
            "/api/plants?search=brav", ApiFactory.Json);
        var descending = await admin.GetFromJsonAsync<PagedResult<PlantSummaryDto>>(
            "/api/plants?sort=-name", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("B1", Assert.Single(found!.Items).Code);
        Assert.Equal("Charlie", descending!.Items[0].Name);
    }

    [Fact]
    public async Task Creating_a_plant_is_written_to_the_audit_log()
    {
        var company = await factory.CreateCompanyAsync();
        var plant = await CreatePlantAsync(company.Admin.Client, "AUD1");

        var audit = await company.Admin.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit", ApiFactory.Json);

        Assert.Contains(audit!, e => e.Action == "plant.created" && e.EntityId == plant.Plant.Id.ToString());
    }
}