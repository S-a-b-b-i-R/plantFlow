using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Pricing;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data;

// Development only: an approved price book for the demo company (recipes, material costs, overheads,
// delivery bands and selling prices), so a floor price can be worked out and orders can be submitted.
// Made-up numbers. Skipped when the company already has any recipe, cost or price of its own.
public static class DemoPriceBookSeeder
{
    private static readonly DateOnly From = new(2026, 1, 1);

    public static async Task EnsurePriceBookAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var tenantId = DemoTenant.Id;

        var products = await db.Products.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ToListAsync();
        var materials = await db.Materials.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).ToListAsync();
        if (products.Count == 0 || materials.Count == 0) return;

        var alreadySetUp =
            await db.MixDesigns.IgnoreQueryFilters().AnyAsync(d => d.TenantId == tenantId)
            || await db.MaterialCosts.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId)
            || await db.OverheadCosts.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId)
            || await db.DeliveryChargeBands.IgnoreQueryFilters().AnyAsync(b => b.TenantId == tenantId)
            || await db.Prices.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId);
        if (alreadySetUp) return;

        var now = DateTime.UtcNow;
        var byCode = materials.ToDictionary(m => m.Code);

        // Cost per kg or litre, in taka.
        var unitCost = new Dictionary<string, decimal>
        {
            ["CEM-OPC"] = 12.0m, ["CEM-PCC"] = 11.0m, ["SND-FINE"] = 0.9m, ["SND-COARSE"] = 1.3m,
            ["STN-12"] = 2.6m, ["STN-20"] = 2.3m, ["STN-40"] = 2.1m, ["WATER"] = 0.02m,
            ["ADM-SP"] = 150m, ["ADD-FLY"] = 4m
        };

        foreach (var (code, cost) in unitCost)
        {
            if (!byCode.TryGetValue(code, out var material)) continue;

            db.MaterialCosts.Add(new MaterialCost
            {
                TenantId = tenantId, MaterialId = material.Id, CostPerUnit = cost, EffectiveFrom = From,
                Status = CostStatus.Approved, ApprovedAtUtc = now
            });
        }

        // One increase that is still waiting for approval, so the approval screen has something to show.
        if (byCode.TryGetValue("CEM-OPC", out var opc))
        {
            db.MaterialCosts.Add(new MaterialCost
            {
                TenantId = tenantId, MaterialId = opc.Id, CostPerUnit = 12.6m,
                EffectiveFrom = new DateOnly(2027, 1, 1), Status = CostStatus.PendingApproval
            });
        }

        db.OverheadCosts.AddRange(
            new OverheadCost
            {
                TenantId = tenantId, Kind = OverheadKind.Overhead, Name = "Plant overhead", AmountPerCft = 8m,
                ValidFrom = From, Status = CostStatus.Approved, ApprovedAtUtc = now
            },
            new OverheadCost
            {
                TenantId = tenantId, Kind = OverheadKind.Fuel, Name = "Fuel", AmountPerCft = 4m,
                ValidFrom = From, Status = CostStatus.Approved, ApprovedAtUtc = now
            });

        var bands = new (decimal From, decimal? To, decimal Charge)[] { (15m, 30m, 6m), (30m, 50m, 12m), (50m, null, 20m) };
        foreach (var (fromKm, toKm, charge) in bands)
        {
            db.DeliveryChargeBands.Add(new DeliveryChargeBand
            {
                TenantId = tenantId, FromKm = fromKm, ToKm = toKm, ChargePerCft = charge,
                Status = CostStatus.Approved, ApprovedAtUtc = now
            });
        }

        // One general recipe per product, built from the strength and the stone size in its code.
        foreach (var product in products)
        {
            var items = RecipeFor(product).Where(i => byCode.ContainsKey(i.Code)).ToList();
            if (items.Count == 0) continue;

            db.MixDesigns.Add(new MixDesign
            {
                TenantId = tenantId, ProductId = product.Id, Version = 1, Kind = MixDesignKind.Standard,
                Status = MixDesignStatus.Approved, ApprovedAtUtc = now, Notes = "Demo recipe",
                Items = items.Select(i => new MixDesignItem
                {
                    TenantId = tenantId, MaterialId = byCode[i.Code].Id, QuantityPerCft = i.Quantity
                }).ToList()
            });

            // Material cost plus overhead and fuel, with about 25% on top, rounded to the nearest 5 taka.
            var cost = items.Sum(i => i.Quantity * unitCost[i.Code]) + 8m + 4m;
            db.Prices.Add(new Price
            {
                TenantId = tenantId, ProductId = product.Id, PricePerCft = Math.Round(cost * 1.25m / 5m) * 5m,
                ValidFrom = From, Status = PriceStatus.Active, ApprovedAtUtc = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static IEnumerable<(string Code, decimal Quantity)> RecipeFor(Product product)
    {
        var psi = int.TryParse(product.StrengthClass.Split(' ')[0], out var value) ? value : 3000;

        // Cement per CFT grows with strength: about 9 kg at 2500 psi and 14.5 kg at 6000 psi.
        var cement = Math.Round(9m + (psi - 2500) / 3500m * 5.5m, 1);
        var stone = product.MaxAggregateMm switch { 12 => "STN-12", 40 => "STN-40", _ => "STN-20" };

        yield return (psi >= 4000 ? "CEM-OPC" : "CEM-PCC", cement);
        yield return ("SND-FINE", product.MaxAggregateMm == 12 ? 21m : 18m);
        yield return ("SND-COARSE", 3m);
        yield return (stone, product.MaxAggregateMm == 12 ? 25m : 28m);
        yield return ("WATER", 5m);
        yield return ("ADM-SP", psi >= 4000 ? 0.07m : 0.04m);
    }
}
