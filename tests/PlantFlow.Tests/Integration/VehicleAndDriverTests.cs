using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class VehicleAndDriverTests(ApiFactory factory)
{
    private static async Task<Guid> CreatePlantAsync(HttpClient client, string code, string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/plants",
            new { code, name = $"Plant {code}", timeZone = "Asia/Dhaka", operatingHoursPerDay = 10, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!.Plant.Id;
    }

    private static async Task<DriverDto> CreateDriverAsync(HttpClient client, string name, string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/drivers", new { name, phone = "01800000000", status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DriverDto>(ApiFactory.Json))!;
    }

    private static Task<HttpResponseMessage> PostVehicleAsync(
        HttpClient client, Guid plantId, string code, string type = "TransitMixer", decimal? capacity = 300,
        string? plate = null, Guid? driverId = null, string status = "Available") =>
        client.PostAsJsonAsync("/api/vehicles", new
        {
            plantId,
            code,
            type,
            capacityCft = capacity,
            plateNumber = plate,
            defaultDriverId = driverId,
            status
        });

    private static async Task<VehicleDto> CreateVehicleAsync(
        HttpClient client, Guid plantId, string code, string type = "TransitMixer", decimal? capacity = 300,
        string? plate = null, Guid? driverId = null)
    {
        var response = await PostVehicleAsync(client, plantId, code, type, capacity, plate, driverId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleDto>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task A_vehicle_shows_its_plant_and_default_driver()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var driver = await CreateDriverAsync(admin, "Karim Uddin");

        var vehicle = await CreateVehicleAsync(admin, plantId, "tm-01", plate: "dhaka-metro-1234", driverId: driver.Id);

        Assert.Equal("TM-01", vehicle.Code);
        Assert.Equal("P1", vehicle.PlantCode);
        Assert.Equal("Karim Uddin", vehicle.DefaultDriverName);
        Assert.Equal("DHAKA-METRO-1234", vehicle.PlateNumber);
    }

    [Fact]
    public async Task A_transit_mixer_needs_a_capacity_but_a_pump_does_not()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");

        var mixerWithoutCapacity = await PostVehicleAsync(admin, plantId, "TM-1", capacity: null);
        var pumpWithoutCapacity = await PostVehicleAsync(admin, plantId, "PU-1", type: "Pump", capacity: null);
        var negative = await PostVehicleAsync(admin, plantId, "TM-2", capacity: -5);

        Assert.Equal(HttpStatusCode.BadRequest, mixerWithoutCapacity.StatusCode);
        Assert.Equal(HttpStatusCode.Created, pumpWithoutCapacity.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task Codes_and_plates_are_unique_within_a_company_only()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var plantA = await CreatePlantAsync(a.Admin.Client, "P1");
        var plantB = await CreatePlantAsync(b.Admin.Client, "P1");

        await CreateVehicleAsync(a.Admin.Client, plantA, "TM-1", plate: "AB-1");

        var duplicateCode = await PostVehicleAsync(a.Admin.Client, plantA, "tm-1", plate: "AB-2");
        var duplicatePlate = await PostVehicleAsync(a.Admin.Client, plantA, "TM-2", plate: "ab-1");
        var noPlateTwice1 = await PostVehicleAsync(a.Admin.Client, plantA, "TM-3");
        var noPlateTwice2 = await PostVehicleAsync(a.Admin.Client, plantA, "TM-4");
        var sameInOtherCompany = await PostVehicleAsync(b.Admin.Client, plantB, "TM-1", plate: "AB-1");

        Assert.Equal(HttpStatusCode.Conflict, duplicateCode.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicatePlate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, noPlateTwice1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, noPlateTwice2.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sameInOtherCompany.StatusCode);
    }

    [Fact]
    public async Task Plant_and_driver_must_be_active_and_from_the_same_company()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var activePlant = await CreatePlantAsync(a.Admin.Client, "P1");
        var inactivePlant = await CreatePlantAsync(a.Admin.Client, "P2", "Inactive");
        var inactiveDriver = await CreateDriverAsync(a.Admin.Client, "Retired Driver", "Inactive");
        var foreignPlant = await CreatePlantAsync(b.Admin.Client, "PB");
        var foreignDriver = await CreateDriverAsync(b.Admin.Client, "Foreign Driver");

        var onInactivePlant = await PostVehicleAsync(a.Admin.Client, inactivePlant, "V1");
        var withInactiveDriver = await PostVehicleAsync(a.Admin.Client, activePlant, "V2", driverId: inactiveDriver.Id);
        var onForeignPlant = await PostVehicleAsync(a.Admin.Client, foreignPlant, "V3");
        var withForeignDriver = await PostVehicleAsync(a.Admin.Client, activePlant, "V4", driverId: foreignDriver.Id);

        Assert.Equal(HttpStatusCode.BadRequest, onInactivePlant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withInactiveDriver.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, onForeignPlant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withForeignDriver.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_vehicle_still_works_after_its_driver_is_deactivated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var driver = await CreateDriverAsync(admin, "Soon Inactive");
        var vehicle = await CreateVehicleAsync(admin, plantId, "TM-1", driverId: driver.Id);

        (await admin.PutAsJsonAsync($"/api/drivers/{driver.Id}", new { name = "Soon Inactive", status = "Inactive" }))
            .EnsureSuccessStatusCode();

        var rename = await admin.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", new
        {
            plantId,
            code = "TM-1B",
            type = "TransitMixer",
            capacityCft = 300,
            defaultDriverId = driver.Id
        });
        var assignInactiveToAnother = await PostVehicleAsync(admin, plantId, "TM-2", driverId: driver.Id);

        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, assignInactiveToAnother.StatusCode);
    }

    [Fact]
    public async Task The_vehicle_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plant1 = await CreatePlantAsync(admin, "P1");
        var plant2 = await CreatePlantAsync(admin, "P2");
        await CreateVehicleAsync(admin, plant1, "TM-1", plate: "AA-100");
        await CreateVehicleAsync(admin, plant1, "TM-2");
        await CreateVehicleAsync(admin, plant2, "TM-3");
        await CreateVehicleAsync(admin, plant1, "PU-1", type: "Pump", capacity: null);

        var firstPage = await admin.GetFromJsonAsync<PagedResult<VehicleDto>>("/api/vehicles?pageSize=3", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<VehicleDto>>("/api/vehicles?pageSize=3&page=2", ApiFactory.Json);
        var byPlant = await admin.GetFromJsonAsync<PagedResult<VehicleDto>>($"/api/vehicles?plantId={plant2}", ApiFactory.Json);
        var pumps = await admin.GetFromJsonAsync<PagedResult<VehicleDto>>("/api/vehicles?type=Pump", ApiFactory.Json);
        var byPlate = await admin.GetFromJsonAsync<PagedResult<VehicleDto>>("/api/vehicles?search=aa-1", ApiFactory.Json);

        Assert.Equal(4, firstPage!.TotalCount);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("TM-3", Assert.Single(byPlant!.Items).Code);
        Assert.Equal("PU-1", Assert.Single(pumps!.Items).Code);
        Assert.Equal("TM-1", Assert.Single(byPlate!.Items).Code);
    }

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(company.Admin.Client, "P1");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var vehicles = await rep.Client.GetAsync("/api/vehicles");
        var drivers = await rep.Client.GetAsync("/api/drivers");
        var createVehicle = await PostVehicleAsync(rep.Client, plantId, "TM-1");
        var createDriver = await rep.Client.PostAsJsonAsync("/api/drivers", new { name = "Nope" });

        Assert.Equal(HttpStatusCode.OK, vehicles.StatusCode);
        Assert.Equal(HttpStatusCode.OK, drivers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createVehicle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createDriver.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_vehicles_or_drivers()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(a.Admin.Client, "P1");
        var driver = await CreateDriverAsync(a.Admin.Client, "Private Driver");
        var vehicle = await CreateVehicleAsync(a.Admin.Client, plantId, "TM-1", driverId: driver.Id);

        var vehiclesSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<VehicleDto>>("/api/vehicles", ApiFactory.Json);
        var driversSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<DriverDto>>("/api/drivers", ApiFactory.Json);
        var getVehicle = await b.Admin.Client.GetAsync($"/api/vehicles/{vehicle.Id}");
        var getDriver = await b.Admin.Client.GetAsync($"/api/drivers/{driver.Id}");
        var putVehicle = await b.Admin.Client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}",
            new { plantId, code = "HACK", type = "TransitMixer", capacityCft = 300 });
        var putDriver = await b.Admin.Client.PutAsJsonAsync($"/api/drivers/{driver.Id}", new { name = "Hacked" });

        Assert.Empty(vehiclesSeenByB!.Items);
        Assert.Empty(driversSeenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, getVehicle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getDriver.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, putVehicle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, putDriver.StatusCode);
    }
}