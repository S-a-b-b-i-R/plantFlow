using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data;

// Made-up data for the demo company, so lists and (later) dashboards are not empty.
// Development only. Nothing here comes from a real company, person or system.
public static class DemoDataSeeder
{
    private static readonly string[] FirstNames =
    [
        "Rahim", "Karim", "Jamal", "Habib", "Salim", "Nasir", "Faruk", "Mizan", "Kamal", "Shakil",
        "Anwar", "Belal", "Delwar", "Emon", "Fahim", "Gias", "Hasan", "Iqbal", "Jahid", "Kabir"
    ];

    private static readonly string[] LastNames =
    [
        "Uddin", "Ahmed", "Hossain", "Mia", "Khan", "Sarker", "Islam", "Molla", "Biswas", "Talukder",
        "Mondal", "Sheikh", "Chowdhury", "Howlader", "Majumder", "Pramanik", "Bhuiyan", "Miah", "Akand", "Dewan"
    ];

    private static readonly string[] CompanyWords =
    [
        "Orion", "Zenith", "Apex", "Vertex", "Pinnacle", "Horizon", "Summit", "Crest", "Landmark", "Keystone",
        "Cornerstone", "Beacon", "Anchor", "Atlas", "Monarch"
    ];

    private static readonly string[] CompanySuffixes = ["Builders", "Developers", "Construction", "Properties", "Engineering"];

    private static readonly string[] ProjectNames =
    [
        "Riverside Tower", "Green Valley Complex", "City Center Plaza", "Lake View Residency", "Sunrise Heights",
        "Metro Business Hub", "Garden Court", "Skyline Apartments", "Harbor Point", "Central Mall",
        "Hillside Villas", "Station Road Bridge"
    ];

    private static readonly string[] Areas =
        ["Uttara", "Gulshan", "Banani", "Mirpur", "Dhanmondi", "Bashundhara", "Purbachal", "Narayanganj"];

    private static string PersonName(int k) => $"{FirstNames[k % 20]} {LastNames[(k * 7 + k / 20) % 20]}";

    private static string Phone(int k) => "017" + (10_000_000 + k * 7_919);

    public static async Task EnsureDemoDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();

        // The Dhaka North plant marks the data as already seeded. Query filters are off because
        // there is no signed-in user at startup.
        if (await db.Plants.IgnoreQueryFilters().AnyAsync(p => p.TenantId == DemoTenant.Id && p.Code == "DHK-N")) return;

        var tenantId = DemoTenant.Id;

        // ---- Plants, batching plants and shifts ----
        var plantSpecs = new (string Code, string Name, string Address, decimal Hours)[]
        {
            ("DHK-N", "Dhaka North Plant", "Uttara Sector 15, Dhaka", 24m),
            ("DHK-S", "Dhaka South Plant", "Demra Industrial Area, Dhaka", 16m),
            ("CTG", "Chattogram Plant", "Patenga Industrial Zone, Chattogram", 20m)
        };

        var plants = plantSpecs.Select(s => new Plant
        {
            TenantId = tenantId,
            Code = s.Code,
            Name = s.Name,
            Address = s.Address,
            TimeZone = "Asia/Dhaka",
            OperatingHoursPerDay = s.Hours
        }).ToList();
        db.Plants.AddRange(plants);

        var batchingPlantSpecs = new (int Plant, string Code, string Model, decimal CftPerHour, int BatchMinutes)[]
        {
            (0, "BP-1", "NW-120", 2100m, 8), (0, "BP-2", "NW-90", 1400m, 8), (0, "BP-3", "NW-60", 1000m, 7),
            (1, "BP-1", "NW-120", 1800m, 8), (1, "BP-2", "NW-60", 1200m, 7),
            (2, "BP-1", "NW-90", 1400m, 8)
        };

        db.BatchingPlants.AddRange(batchingPlantSpecs.Select(s => new BatchingPlant
        {
            TenantId = tenantId,
            PlantId = plants[s.Plant].Id,
            Code = s.Code,
            Make = "Northwind Machinery",
            Model = s.Model,
            RatedCapacityCftPerHour = s.CftPerHour,
            BatchDurationMinutes = s.BatchMinutes
        }));

        var shiftSpecs = new (int Plant, string Name, string Start, string End)[]
        {
            (0, "Morning", "06:00", "14:00"), (0, "Afternoon", "14:00", "22:00"), (0, "Evening", "22:00", "06:00"),
            (1, "Morning", "07:00", "15:00"), (1, "Afternoon", "15:00", "23:00"),
            (2, "Morning", "06:00", "14:00"), (2, "Afternoon", "14:00", "20:00"), (2, "Evening", "20:00", "02:00")
        };

        db.PlantShifts.AddRange(shiftSpecs.Select(s => new PlantShift
        {
            TenantId = tenantId,
            PlantId = plants[s.Plant].Id,
            Name = s.Name,
            StartTime = TimeOnly.Parse(s.Start),
            EndTime = TimeOnly.Parse(s.End)
        }));

        // ---- Drivers and vehicles: every vehicle gets its own default driver ----
        var vehicleSpecs = new (int Plant, int Mixers, int Pumps)[] { (0, 14, 3), (1, 10, 3), (2, 8, 2) };
        decimal[] mixerCapacities = [282m, 353m, 250m];
        var sequence = 0;
        var mixerNumber = 0;
        var pumpNumber = 0;

        foreach (var (plantIndex, mixers, pumps) in vehicleSpecs)
        {
            var platePrefix = plantIndex == 2 ? "CTG" : "DHK";

            for (var i = 0; i < mixers + pumps; i++)
            {
                var isMixer = i < mixers;
                var code = isMixer ? $"TM-{++mixerNumber:00}" : $"PU-{++pumpNumber:00}";

                var driver = new Driver { TenantId = tenantId, Name = PersonName(sequence), Phone = Phone(sequence) };
                db.Drivers.Add(driver);

                var status = code switch
                {
                    "TM-07" or "PU-03" => VehicleStatus.Maintenance,
                    "TM-32" => VehicleStatus.Retired,
                    _ => VehicleStatus.Available
                };

                db.Vehicles.Add(new Vehicle
                {
                    TenantId = tenantId,
                    PlantId = plants[plantIndex].Id,
                    Code = code,
                    Type = isMixer ? VehicleType.TransitMixer : VehicleType.Pump,
                    CapacityCft = isMixer ? mixerCapacities[mixerNumber % 3] : null,
                    PlateNumber = $"{platePrefix}-MX-{1000 + sequence * 13}",
                    DefaultDriverId = status == VehicleStatus.Retired ? null : driver.Id,
                    Status = status
                });

                sequence++;
            }
        }

        // ---- Catalog: products, materials and service charges ----
        var products = new (string Code, string Strength, int AggregateMm, int SlumpMm)[]
        {
            ("G2500-20", "2500 psi", 20, 100), ("G3000-20", "3000 psi", 20, 100), ("G3500-20", "3500 psi", 20, 100),
            ("G4000-20", "4000 psi", 20, 100), ("G4500-20", "4500 psi", 20, 100), ("G5000-20", "5000 psi", 20, 100),
            ("G6000-20", "6000 psi", 20, 100), ("G3000-12", "3000 psi", 12, 150), ("G4000-12", "4000 psi", 12, 150),
            ("G5000-12", "5000 psi", 12, 150), ("G3500-40", "3500 psi", 40, 75), ("G4000-40", "4000 psi", 40, 75)
        };

        db.Products.AddRange(products.Select(p => new Product
        {
            TenantId = tenantId,
            Code = p.Code,
            Name = $"{p.Strength}, {p.AggregateMm} mm",
            StrengthClass = p.Strength.ToUpperInvariant(),
            MaxAggregateMm = p.AggregateMm,
            DefaultSlumpMm = p.SlumpMm
        }));

        var materials = new (string Code, string Name, MaterialCategory Category, MaterialUnit Unit)[]
        {
            ("CEM-OPC", "Ordinary Portland cement", MaterialCategory.Cement, MaterialUnit.Kg),
            ("CEM-PCC", "Portland composite cement", MaterialCategory.Cement, MaterialUnit.Kg),
            ("SND-FINE", "Fine sand", MaterialCategory.Sand, MaterialUnit.Kg),
            ("SND-COARSE", "Coarse sand", MaterialCategory.Sand, MaterialUnit.Kg),
            ("STN-12", "Stone chips 12 mm", MaterialCategory.Stone, MaterialUnit.Kg),
            ("STN-20", "Stone chips 20 mm", MaterialCategory.Stone, MaterialUnit.Kg),
            ("STN-40", "Stone chips 40 mm", MaterialCategory.Stone, MaterialUnit.Kg),
            ("WATER", "Water", MaterialCategory.Water, MaterialUnit.Litre),
            ("ADM-SP", "Superplasticizer", MaterialCategory.Admixture, MaterialUnit.Litre),
            ("ADD-FLY", "Fly ash", MaterialCategory.Additive, MaterialUnit.Kg)
        };

        db.Materials.AddRange(materials.Select(m => new Material
        {
            TenantId = tenantId,
            Code = m.Code,
            Name = m.Name,
            Category = m.Category,
            Unit = m.Unit
        }));

        db.ServiceCharges.AddRange(
            new ServiceCharge { TenantId = tenantId, Code = "PUMP", Name = "Pump charge", Kind = ServiceChargeKind.Pump, Basis = ChargeBasis.PerCft, DefaultAmount = 15m },
            new ServiceCharge { TenantId = tenantId, Code = "NIGHT", Name = "Night delivery", Kind = ServiceChargeKind.NightDelivery, Basis = ChargeBasis.PerLoad, DefaultAmount = 1500m },
            new ServiceCharge { TenantId = tenantId, Code = "SHORT", Name = "Short load", Kind = ServiceChargeKind.ShortLoad, Basis = ChargeBasis.PerLoad, DefaultAmount = 2500m },
            new ServiceCharge { TenantId = tenantId, Code = "DIST", Name = "Extra distance", Kind = ServiceChargeKind.ExtraDistance, Basis = ChargeBasis.PerCft, DefaultAmount = 4.5m });

        // ---- Sales reps: one manager and five reps who report to him ----
        var manager = new SalesRep { TenantId = tenantId, Name = "Abdul Karim", Region = "All regions" };
        db.SalesReps.Add(manager);

        var repSpecs = new (string Name, string Region)[]
        {
            ("Nusrat Jahan", "Dhaka North"), ("Tanvir Hasan", "Dhaka South"), ("Farhana Akter", "Gazipur"),
            ("Imran Hossain", "Narayanganj"), ("Sadia Rahman", "Chattogram")
        };

        var reps = repSpecs.Select(r => new SalesRep
        {
            TenantId = tenantId,
            Name = r.Name,
            Region = r.Region,
            ManagerId = manager.Id
        }).ToList();
        db.SalesReps.AddRange(reps);

        // ---- Customers and their sites ----
        for (var i = 0; i < 30; i++)
        {
            var kind = i % 10;
            var type = kind < 6 ? CustomerType.Contractor : kind < 9 ? CustomerType.Developer : CustomerType.Individual;
            var name = type == CustomerType.Individual
                ? $"{PersonName(100 + i)} (residence)"
                : $"{CompanyWords[i % 15]} {CompanySuffixes[(i + i / 15) % 5]}";

            var customer = new Customer
            {
                TenantId = tenantId,
                Code = $"CUST-{i + 1:000}",
                Name = name,
                Type = type,
                Phone = Phone(200 + i),
                Email = type == CustomerType.Individual ? null : $"office{i + 1:00}@example.com",
                SalesRepId = reps[i % reps.Count].Id
            };
            db.Customers.Add(customer);

            var siteCount = 1 + i % 4;
            for (var j = 0; j < siteCount; j++)
            {
                db.Sites.Add(new Site
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    Name = ProjectNames[(i * 3 + j * 5) % ProjectNames.Length],
                    Address = $"Plot {10 + i + j}, Road {j + 1}, {Areas[(i + j) % Areas.Length]}, Dhaka",
                    Latitude = 23.70m + (i * 7 + j * 3) % 20 * 0.01m,
                    Longitude = 90.35m + (i * 5 + j * 11) % 20 * 0.01m,
                    ContactName = PersonName(300 + i * 4 + j),
                    ContactPhone = Phone(400 + i * 4 + j),
                    AccessNotes = j % 2 == 0 ? "Narrow lane, trucks up to 282 CFT only." : null
                });
            }
        }

        await db.SaveChangesAsync();
    }
}