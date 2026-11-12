using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class PlantShiftApiTests(ApiFactory factory)
{
    private static async Task<Guid> CreatePlantAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/plants",
            new { code, name = $"Plant {code}", timeZone = "Asia/Dhaka", operatingHoursPerDay = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!.Plant.Id;
    }

    private static Task<HttpResponseMessage> PostShiftAsync(
        HttpClient client, Guid plantId, string name, string start, string end, string status = "Active") =>
        client.PostAsJsonAsync($"/api/plants/{plantId}/shifts", new { name, startTime = start, endTime = end, status });

    private static async Task<PlantShiftDto> CreateShiftAsync(
        HttpClient client, Guid plantId, string name, string start, string end)
    {
        var response = await PostShiftAsync(client, plantId, name, start, end);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantShiftDto>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task Three_shifts_can_cover_the_day_and_the_evening_can_run_past_midnight()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");

        // Created out of order on purpose: the list must come back in start-time order.
        await CreateShiftAsync(admin, plantId, "Evening", "22:00", "06:00");
        await CreateShiftAsync(admin, plantId, "Morning", "06:00", "14:00");
        await CreateShiftAsync(admin, plantId, "Afternoon", "14:00", "22:00");

        var shifts = await admin.GetFromJsonAsync<List<PlantShiftDto>>($"/api/plants/{plantId}/shifts", ApiFactory.Json);

        Assert.Equal(["Morning", "Afternoon", "Evening"], shifts!.Select(s => s.Name));
        Assert.All(shifts, s => Assert.Equal(480, s.DurationMinutes));
        Assert.Equal([false, false, true], shifts.Select(s => s.CrossesMidnight));
    }

    [Fact]
    public async Task Active_shifts_of_one_plant_cannot_overlap_even_across_midnight()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        await CreateShiftAsync(admin, plantId, "Morning", "06:00", "14:00");
        await CreateShiftAsync(admin, plantId, "Evening", "22:00", "06:00");

        var overlapsMorning = await PostShiftAsync(admin, plantId, "Midday", "13:00", "15:00");
        var overlapsNightEnd = await PostShiftAsync(admin, plantId, "Early", "04:00", "07:00");
        var fillsTheGap = await PostShiftAsync(admin, plantId, "Afternoon", "14:00", "22:00");

        Assert.Equal(HttpStatusCode.Conflict, overlapsMorning.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overlapsNightEnd.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fillsTheGap.StatusCode);
    }

    [Fact]
    public async Task A_retired_shift_no_longer_blocks_a_replacement_but_cannot_be_reactivated_over_it()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var afternoon = await CreateShiftAsync(admin, plantId, "Afternoon", "14:00", "22:00");

        var retire = await admin.PutAsJsonAsync($"/api/plant-shifts/{afternoon.Id}",
            new { name = "Afternoon", startTime = "14:00", endTime = "22:00", status = "Inactive" });
        var replacement = await PostShiftAsync(admin, plantId, "Late afternoon", "15:00", "21:00");
        var reactivate = await admin.PutAsJsonAsync($"/api/plant-shifts/{afternoon.Id}",
            new { name = "Afternoon", startTime = "14:00", endTime = "22:00", status = "Active" });

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
    }

    [Fact]
    public async Task Shift_input_is_validated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");

        var sameStartAndEnd = await PostShiftAsync(admin, plantId, "Zero", "06:00", "06:00");
        var withSeconds = await PostShiftAsync(admin, plantId, "Seconds", "06:00:30", "14:00");
        var notATime = await PostShiftAsync(admin, plantId, "Garbage", "banana", "14:00");
        var missingEnd = await admin.PostAsJsonAsync($"/api/plants/{plantId}/shifts", new { name = "No end", startTime = "06:00" });
        var shortName = await PostShiftAsync(admin, plantId, "A", "06:00", "14:00");

        Assert.Equal(HttpStatusCode.BadRequest, sameStartAndEnd.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withSeconds.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notATime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingEnd.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortName.StatusCode);
    }

    [Fact]
    public async Task Shift_names_are_unique_per_plant_but_can_repeat_across_plants()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plant1 = await CreatePlantAsync(admin, "P1");
        var plant2 = await CreatePlantAsync(admin, "P2");

        await CreateShiftAsync(admin, plant1, "Morning", "06:00", "14:00");

        var duplicate = await PostShiftAsync(admin, plant1, "morning", "15:00", "20:00");
        var otherPlant = await PostShiftAsync(admin, plant2, "Morning", "06:00", "14:00");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherPlant.StatusCode);
    }

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(company.Admin.Client, "P1");
        var shift = await CreateShiftAsync(company.Admin.Client, plantId, "Morning", "06:00", "14:00");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var list = await rep.Client.GetAsync($"/api/plants/{plantId}/shifts");
        var create = await PostShiftAsync(rep.Client, plantId, "Nope", "15:00", "20:00");
        var update = await rep.Client.PutAsJsonAsync($"/api/plant-shifts/{shift.Id}",
            new { name = "Morning", startTime = "06:00", endTime = "13:00" });

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_shifts()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(a.Admin.Client, "P1");
        var shift = await CreateShiftAsync(a.Admin.Client, plantId, "Morning", "06:00", "14:00");

        var list = await b.Admin.Client.GetAsync($"/api/plants/{plantId}/shifts");
        var create = await PostShiftAsync(b.Admin.Client, plantId, "Injected", "15:00", "20:00");
        var update = await b.Admin.Client.PutAsJsonAsync($"/api/plant-shifts/{shift.Id}",
            new { name = "Hacked", startTime = "06:00", endTime = "14:00" });

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }
}