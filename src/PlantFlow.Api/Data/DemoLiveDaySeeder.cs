using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data;

// Development only: builds an approved shift plan for Dhaka North around the current time,
// so the live board has something to show. Runs once per shift; restarting does not duplicate it.
public static class DemoLiveDaySeeder
{
    private const int TravelMinutes = 24;   // 10 km at 25 km/h
    private const int PumpSetupMinutes = 90;

    public static async Task EnsureLiveDayAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var tenantId = DemoTenant.Id;

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        var plant = await db.Plants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == "DHK-N");
        if (plant is null) return;

        var nowUtc = DateTime.UtcNow;
        var nowLocal = nowUtc.AddMinutes(tenant.UtcOffsetMinutes);

        // The shift that contains the current local time. An overnight shift belongs to the day it starts on.
        var shifts = await db.PlantShifts.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.PlantId == plant.Id).ToListAsync();
        PlantShift? shift = null;
        var planDate = default(DateOnly);
        foreach (var candidate in shifts)
        {
            foreach (var day in new[] { DateOnly.FromDateTime(nowLocal), DateOnly.FromDateTime(nowLocal).AddDays(-1) })
            {
                var start = day.ToDateTime(candidate.StartTime);
                if (nowLocal >= start && nowLocal < start.AddMinutes(candidate.DurationMinutes))
                {
                    shift = candidate;
                    planDate = day;
                }
            }
        }
        if (shift is null) return;

        var exists = await db.ShiftPlans.IgnoreQueryFilters().AnyAsync(
            p => p.TenantId == tenantId && p.PlantId == plant.Id && p.Date == planDate && p.ShiftId == shift.Id);
        if (exists) return;

        var mixers = await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.Type == VehicleType.TransitMixer)
            .OrderBy(v => v.PlateNumber).Take(4).ToListAsync();
        var pumps = await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.Type == VehicleType.Pump)
            .OrderBy(v => v.PlateNumber).Take(2).ToListAsync();
        var sites = await db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId).OrderBy(s => s.Id).Take(4).ToListAsync();
        var productIds = await db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId).OrderBy(p => p.Code).Select(p => p.Id).Take(4).ToListAsync();
        if (mixers.Count < 4 || pumps.Count < 2 || sites.Count < 4 || productIds.Count < 4) return;

        DateTime Local(int minutesFromNow) =>
            DateTime.SpecifyKind(nowLocal.AddMinutes(minutesFromNow), DateTimeKind.Unspecified);
        DateTime Utc(int minutesFromNow) => nowUtc.AddMinutes(minutesFromNow);

        var sequence = (await db.Orders.IgnoreQueryFilters().Where(o => o.TenantId == tenantId)
            .MaxAsync(o => (int?)o.Sequence) ?? 0);

        var plan = new ShiftPlan
        {
            TenantId = tenantId,
            PlantId = plant.Id,
            Date = planDate,
            ShiftId = shift.Id,
            Status = ShiftPlanStatus.Approved,
            ApprovedAtUtc = nowUtc.AddHours(-3),
        };
        db.ShiftPlans.Add(plan);

        // Starts are minutes from now. Pump leaves 90 minutes of setup plus the drive before the start.
        var specs = new[]
        {
            (Start: -40, Cft: 800m, Loads: 3, Pump: (Vehicle?)pumps[0], Status: OrderStatus.InProgress),
            (Start: -10, Cft: 400m, Loads: 2, Pump: (Vehicle?)pumps[1], Status: OrderStatus.Confirmed),
            (Start: 150, Cft: 600m, Loads: 3, Pump: (Vehicle?)pumps[0], Status: OrderStatus.Confirmed),
            (Start: 110, Cft: 300m, Loads: 2, Pump: (Vehicle?)null,     Status: OrderStatus.Confirmed),
        };

        var pours = new List<Pour>();
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            sequence++;
            var order = new Order
            {
                TenantId = tenantId,
                Sequence = sequence,
                Number = $"SO-{sequence:D6}",
                CustomerId = sites[i].CustomerId,
                SiteId = sites[i].Id,
                PlantId = plant.Id,
                RequestedDate = planDate,
                DistanceKm = 10,
                Status = spec.Status,
                PaymentTerms = "Net 30",
                ConfirmedAtUtc = nowUtc.AddHours(-5),
                ConfirmationMethod = ConfirmationMethod.Phone,
            };
            var line = new OrderLine
            {
                TenantId = tenantId,
                OrderId = order.Id,
                ProductId = productIds[i],
                QuantityCft = spec.Cft,
                UnitPrice = 450,
                SlumpMm = 150,
                PourElement = PourElement.Slab,
                PourMethod = spec.Pump is null ? PourMethod.Chute : PourMethod.Pump,
                DeliveredCft = i == 0 ? 282 : 0, // The first pour has one load finished.
            };
            db.Orders.Add(order);
            db.OrderLines.Add(line);

            pours.Add(new Pour
            {
                TenantId = tenantId,
                ShiftPlanId = plan.Id,
                OrderLineId = line.Id,
                PlannedCft = spec.Cft,
                PlannedStart = Local(spec.Start),
                PumpId = spec.Pump?.Id,
                PumpLeaveAt = spec.Pump is null ? null : Local(spec.Start - PumpSetupMinutes - TravelMinutes),
                FirstTruckLeaveAt = Local(spec.Start - TravelMinutes),
                EstimatedTruckLoads = spec.Loads,
            });
        }
        db.Pours.AddRange(pours);

        // Pour 1: running well. Pump on time, first truck on time, pour started two minutes early.
        pours[0].PumpDispatchedAtUtc = Utc(-154);
        pours[0].PumpArrivedAtUtc = Utc(-120);
        pours[0].PumpAssembledAtUtc = Utc(-60);
        db.TruckLoads.AddRange(
            new TruckLoad
            {
                TenantId = tenantId,
                PourId = pours[0].Id,
                TruckId = mixers[0].Id,
                Number = 1,
                QuantityCft = 282,
                ProducedAtUtc = Utc(-66),
                DispatchedAtUtc = Utc(-64),
                ArrivedAtUtc = Utc(-42),
                PouringStartedAtUtc = Utc(-42),
                PouringEndedAtUtc = Utc(-8),
            },
            new TruckLoad
            {
                TenantId = tenantId,
                PourId = pours[0].Id,
                TruckId = mixers[1].Id,
                Number = 2,
                QuantityCft = 282,
                ProducedAtUtc = Utc(-37),
                DispatchedAtUtc = Utc(-35),
                ArrivedAtUtc = Utc(-15),
                PouringStartedAtUtc = Utc(-10),
            },
            new TruckLoad
            {
                TenantId = tenantId,
                PourId = pours[0].Id,
                TruckId = mixers[2].Id,
                Number = 3,
                QuantityCft = 236,
                ProducedAtUtc = Utc(-3),
                DispatchedAtUtc = Utc(-2),
            });

        // Pour 2: late. The pump left 35 minutes late, is on site but not assembled, and the pour has not started.
        pours[1].PumpDispatchedAtUtc = Utc(-89);
        pours[1].PumpArrivedAtUtc = Utc(-12);
        db.TruckLoads.Add(new TruckLoad
        {
            TenantId = tenantId,
            PourId = pours[1].Id,
            TruckId = mixers[3].Id,
            Number = 1,
            QuantityCft = 282,
            ProducedAtUtc = Utc(-7),
            DispatchedAtUtc = Utc(-5),
        });

        // Pours 3 and 4 are still ahead: nothing is due yet.
        await db.SaveChangesAsync();
    }
}