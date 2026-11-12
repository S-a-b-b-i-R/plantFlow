using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class DemoDataTests(ApiFactory factory)
{
    private async Task SeedAsync()
    {
        await DbSeeder.EnsureDemoTenantAsync(factory.Services);
        await DemoDataSeeder.EnsureDemoDataAsync(factory.Services);
    }

    private static IQueryable<T> OfDemo<T>(DbSet<T> set) where T : class, PlantFlow.Api.Domain.Common.ITenantOwned =>
        set.IgnoreQueryFilters().Where(e => e.TenantId == DemoTenant.Id);

    [Fact]
    public async Task The_demo_company_gets_the_expected_amount_of_data()
    {
        await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();

        Assert.Equal(3, await OfDemo(db.Plants).CountAsync(p => p.Code == "DHK-N" || p.Code == "DHK-S" || p.Code == "CTG"));
        Assert.Equal(6, await OfDemo(db.BatchingPlants).CountAsync());
        Assert.Equal(8, await OfDemo(db.PlantShifts).CountAsync());
        Assert.Equal(6, await OfDemo(db.SalesReps).CountAsync());
        Assert.Equal(40, await OfDemo(db.Drivers).CountAsync());
        Assert.Equal(32, await OfDemo(db.Vehicles).CountAsync(v => v.Type == VehicleType.TransitMixer));
        Assert.Equal(8, await OfDemo(db.Vehicles).CountAsync(v => v.Type == VehicleType.Pump));
        Assert.Equal(12, await OfDemo(db.Products).CountAsync());
        Assert.Equal(10, await OfDemo(db.Materials).CountAsync());
        Assert.Equal(4, await OfDemo(db.ServiceCharges).CountAsync());
        Assert.Equal(30, await OfDemo(db.Customers).CountAsync());
        Assert.Equal(73, await OfDemo(db.Sites).CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_anything()
    {
        await SeedAsync();
        await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();

        Assert.Equal(40, await OfDemo(db.Vehicles).CountAsync());
        Assert.Equal(30, await OfDemo(db.Customers).CountAsync());
    }

    [Fact]
    public async Task The_demo_data_follows_the_business_rules()
    {
        await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();

        // Shifts of one plant never overlap, and each plant's operating hours match its shifts.
        var plants = await OfDemo(db.Plants).Where(p => p.Code == "DHK-N" || p.Code == "DHK-S" || p.Code == "CTG").ToListAsync();
        var shifts = await OfDemo(db.PlantShifts).ToListAsync();
        foreach (var plant in plants)
        {
            var plantShifts = shifts.Where(s => s.PlantId == plant.Id).ToList();
            Assert.NotEmpty(plantShifts);
            Assert.Equal(plant.OperatingHoursPerDay, plantShifts.Sum(s => s.DurationMinutes) / 60m);

            for (var i = 0; i < plantShifts.Count; i++)
            {
                for (var j = i + 1; j < plantShifts.Count; j++)
                {
                    Assert.False(plantShifts[i].OverlapsWith(plantShifts[j]),
                        $"{plant.Code}: {plantShifts[i].Name} overlaps {plantShifts[j].Name}");
                }
            }
        }

        // Every transit mixer has a capacity, pumps do not need one.
        var vehicles = await OfDemo(db.Vehicles).ToListAsync();
        Assert.All(vehicles.Where(v => v.Type == VehicleType.TransitMixer), v => Assert.NotNull(v.CapacityCft));

        // Sales reps all report to the manager, and every customer has a rep.
        var reps = await OfDemo(db.SalesReps).ToListAsync();
        var manager = Assert.Single(reps, r => r.ManagerId is null);
        Assert.All(reps.Where(r => r.Id != manager.Id), r => Assert.Equal(manager.Id, r.ManagerId));
        Assert.All(await OfDemo(db.Customers).ToListAsync(), c => Assert.NotNull(c.SalesRepId));
    }
}