using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// Fleet rules: who manages it, drivers only on mixers, pump operators and crews, and vehicles that
// can't be taken out of use while they have work.
public partial class PourPlanningTests
{
    private static Task<HttpResponseMessage> PutVehicleAsync(
        HttpClient c, Guid id, Guid plantId, string code, string type, string status = "Available",
        decimal? capacityCft = null, Guid? driverId = null) =>
        c.PutAsJsonAsync($"/api/vehicles/{id}",
            new { plantId, code, type, capacityCft, defaultDriverId = driverId, status });

    private static Task<HttpResponseMessage> PostOperatorAsync(HttpClient c, string name, string? phone, Guid? pumpId) =>
        c.PostAsJsonAsync("/api/pump-operators", new { name, phone, pumpId });

    [Fact]
    public async Task Only_plant_managers_and_admins_manage_the_fleet()
    {
        var company = await factory.CreateCompanyAsync();
        var plantId = await CreatePlantAsync(company.Admin.Client, "P1");
        var manager = (await factory.AddMemberAsync(company, "Plant Manager")).Client;
        var planner = (await factory.AddMemberAsync(company, "Planner")).Client;

        var pump = await CreatePumpAsync(manager, plantId, "PU-1");
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/drivers", new { name = "Rahim" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostOperatorAsync(manager, "Jewel", "01711000000", pump)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.PostAsJsonAsync("/api/vehicles", new { plantId, code = "PU-2", type = "Pump" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.PostAsJsonAsync("/api/drivers", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostOperatorAsync(planner, "Nope", "01711000000", pump)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync("/api/pump-operators")).StatusCode);
    }

    [Fact]
    public async Task Drivers_go_on_mixers_only()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var driver = await admin.PostAsJsonAsync("/api/drivers", new { name = "Rahim" });
        var driverId = (await driver.Content.ReadFromJsonAsync<DriverDto>(ApiFactory.Json))!.Id;

        var pumpWithDriver = await admin.PostAsJsonAsync("/api/vehicles",
            new { plantId, code = "PU-1", type = "Pump", defaultDriverId = driverId });
        var pump = await CreatePumpAsync(admin, plantId, "PU-2");
        var giveDriver = await PutVehicleAsync(admin, pump, plantId, "PU-2", "Pump", driverId: driverId);
        var mixer = await admin.PostAsJsonAsync("/api/vehicles",
            new { plantId, code = "TM-1", type = "TransitMixer", capacityCft = 300m, defaultDriverId = driverId });

        Assert.Equal(HttpStatusCode.BadRequest, pumpWithDriver.StatusCode);
        Assert.Contains(VehiclesController.PumpDriverRefused, await pumpWithDriver.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, giveDriver.StatusCode);
        Assert.Equal(HttpStatusCode.Created, mixer.StatusCode);

        var listed = await admin.GetFromJsonAsync<DriverDto>($"/api/drivers/{driverId}", ApiFactory.Json);
        Assert.Equal(["TM-1"], listed!.MixerCodes);
    }

    [Fact]
    public async Task A_pump_operator_needs_a_phone_and_is_on_one_pumps_crew_at_most()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var plantId = await CreatePlantAsync(admin, "P1");
        var pump1 = await CreatePumpAsync(admin, plantId, "PU-1");
        var pump2 = await CreatePumpAsync(admin, plantId, "PU-2");
        var mixer = await CreateMixerAsync(admin, plantId, "TM-1");

        Assert.Equal(HttpStatusCode.BadRequest, (await PostOperatorAsync(admin, "Jewel", null, pump1)).StatusCode);
        var onMixer = await PostOperatorAsync(admin, "Jewel", "01711000000", mixer);
        Assert.Equal(HttpStatusCode.BadRequest, onMixer.StatusCode);
        Assert.Contains(PumpOperatorsController.PumpOnly, await onMixer.Content.ReadAsStringAsync());

        var created = await PostOperatorAsync(admin, "Jewel", "01711000000", pump1);
        var jewel = (await created.Content.ReadFromJsonAsync<PumpOperatorDto>(ApiFactory.Json))!;
        Assert.Equal("PU-1", jewel.PumpCode);
        Assert.Equal(1, (await admin.GetFromJsonAsync<VehicleDto>($"/api/vehicles/{pump1}", ApiFactory.Json))!.CrewCount);

        // Moving Jewel to PU-2 takes him off PU-1's crew.
        var moved = await admin.PutAsJsonAsync($"/api/pump-operators/{jewel.Id}",
            new { name = "Jewel", phone = "01711000000", pumpId = pump2 });
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<PumpOperatorDto>>($"/api/vehicles/{pump1}/crew", ApiFactory.Json))!);
        Assert.Single((await admin.GetFromJsonAsync<List<PumpOperatorDto>>($"/api/vehicles/{pump2}/crew", ApiFactory.Json))!);

        // An inactive operator drops out of the crew.
        (await admin.PutAsJsonAsync($"/api/pump-operators/{jewel.Id}",
            new { name = "Jewel", phone = "01711000000", pumpId = pump2, status = "Inactive" })).EnsureSuccessStatusCode();
        Assert.Empty((await admin.GetFromJsonAsync<List<PumpOperatorDto>>($"/api/vehicles/{pump2}/crew", ApiFactory.Json))!);

        // Another company can't see or change him, or put its operators on this company's pump.
        var foreign = other.Admin.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/pump-operators/{jewel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync($"/api/pump-operators/{jewel.Id}",
            new { name = "Taken", phone = "01711000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostOperatorAsync(foreign, "Spy", "01711000000", pump1)).StatusCode);
        Assert.Empty((await foreign.GetFromJsonAsync<PagedResult<PumpOperatorDto>>("/api/pump-operators", ApiFactory.Json))!.Items);
    }

    [Fact]
    public async Task A_pump_with_pours_ahead_cannot_be_taken_out_of_use_or_moved()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var fx = await SetupAsync(admin);
        var otherPlant = await CreatePlantAsync(admin, "P2");
        var plan = await PlanAsync(admin, fx.W.PlantId, DaysAhead(10), fx.Morning);
        await ReadPourAsync(await PourAsync(admin, plan.Plan.Id, fx.PumpLine, 20m, "09:00:00", fx.Pump));

        var maintenance = await PutVehicleAsync(admin, fx.Pump, fx.W.PlantId, "PMP1", "Pump", "Maintenance");
        var retired = await PutVehicleAsync(admin, fx.Pump, fx.W.PlantId, "PMP1", "Pump", "Retired");
        var moved = await PutVehicleAsync(admin, fx.Pump, otherPlant, "PMP1", "Pump");
        var renamed = await PutVehicleAsync(admin, fx.Pump, fx.W.PlantId, "PMP1A", "Pump");

        Assert.Equal(HttpStatusCode.Conflict, maintenance.StatusCode);
        Assert.Contains("1 pour is planned for PMP1 from", await maintenance.Content.ReadAsStringAsync());
        Assert.Contains("Move them to another pump before you send it for maintenance.",
            await maintenance.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, retired.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        // A pump with nothing ahead can go for maintenance.
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutVehicleAsync(admin, fx.Pump2, fx.W.PlantId, "PMP2", "Pump", "Maintenance")).StatusCode);
    }

    [Fact]
    public async Task A_mixer_out_with_a_load_cannot_be_taken_out_of_use_until_it_is_back()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await ApprovedPourAsync(admin);
        var plantId = (await OperatorViewAsync(admin, s.PlanId)).PlantId;
        var truck = await admin.GetFromJsonAsync<VehicleDto>($"/api/vehicles/{s.Truck}", ApiFactory.Json);

        var produced = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", new { truckId = s.Truck });
        Assert.Equal(HttpStatusCode.Created, produced.StatusCode);
        var load = (await produced.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;

        var retire = await PutVehicleAsync(admin, s.Truck, plantId, truck!.Code, "TransitMixer", "Retired", truck.CapacityCft);
        Assert.Equal(HttpStatusCode.Conflict, retire.StatusCode);
        Assert.Contains($"{truck.Code} is out with a load.", await retire.Content.ReadAsStringAsync());

        // Back at the plant: here the load is cancelled before it left.
        (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/cancel", new { reason = "Changed our mind" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutVehicleAsync(admin, s.Truck, plantId, truck.Code, "TransitMixer", "Retired", truck.CapacityCft)).StatusCode);
    }
}
