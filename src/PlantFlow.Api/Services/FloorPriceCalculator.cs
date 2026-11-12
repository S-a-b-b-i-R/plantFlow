using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;

namespace PlantFlow.Api.Services;

public record MaterialCostLine(string MaterialCode, MaterialUnit Unit, decimal QuantityPerCft, decimal CostPerUnit, decimal Cost);

public record FloorPriceBreakdown(
    Guid ProductId, Guid PlantId, DateOnly Date, decimal DistanceKm, CustomerType? CustomerType,
    Guid MixDesignId, int MixDesignVersion, List<MaterialCostLine> Materials,
    decimal MaterialCost, decimal Overhead, decimal Fuel, decimal BridgeToll, decimal Delivery, decimal TotalCost,
    decimal MarginPercent, decimal AitPercent, decimal FloorPrice,
    decimal VatPercent, decimal Vat, decimal FloorPriceWithVat);

// Either a breakdown, or a plain-language reason why the price cannot be worked out yet.
public record FloorPriceResult(FloorPriceBreakdown? Breakdown, string? Error);

// The lowest price per CFT that still covers cost, AIT and the minimum margin. VAT is shown on top.
public class FloorPriceCalculator(PlantFlowDbContext db)
{
    public async Task<FloorPriceResult> CalculateAsync(
        Guid productId, Guid plantId, DateOnly date, decimal distanceKm, CustomerType? customerType,
        CancellationToken cancellationToken)
    {
        // A recipe for this plant beats the general one.
        var design = await db.MixDesigns.AsNoTracking()
            .Include(d => d.Items).ThenInclude(i => i.Material)
            .Where(d => d.ProductId == productId && d.Kind == MixDesignKind.Standard
                        && d.Status == MixDesignStatus.Approved && (d.PlantId == plantId || d.PlantId == null))
            .OrderBy(d => d.PlantId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (design is null)
        {
            return new(null, "This product has no approved mix design for this plant.");
        }

        var materialIds = design.Items.Select(i => i.MaterialId).ToList();
        var costs = await db.MaterialCosts.AsNoTracking()
            .Where(c => materialIds.Contains(c.MaterialId) && c.EffectiveFrom <= date
                        && (c.PlantId == plantId || c.PlantId == null))
            .ToListAsync(cancellationToken);

        var lines = new List<MaterialCostLine>();
        foreach (var item in design.Items.OrderBy(i => i.Material.Code))
        {
            var cost = costs.Where(c => c.MaterialId == item.MaterialId)
                .OrderBy(c => c.PlantId == null).ThenByDescending(c => c.EffectiveFrom)
                .FirstOrDefault();

            if (cost is null)
            {
                return new(null, $"No cost is set for material {item.Material.Code} on {date:yyyy-MM-dd}.");
            }

            lines.Add(new(item.Material.Code, item.Material.Unit, item.QuantityPerCft, cost.CostPerUnit,
                item.QuantityPerCft * cost.CostPerUnit));
        }

        var overheads = await db.OverheadCosts.AsNoTracking()
            .Where(c => c.ValidFrom <= date && (c.ValidTo == null || c.ValidTo >= date)
                        && (c.PlantId == plantId || c.PlantId == null))
            .ToListAsync(cancellationToken);

        decimal SumOf(OverheadKind kind) => overheads.Where(c => c.Kind == kind).Sum(c => c.AmountPerCft);

        // A plant's own bands replace the general bands.
        var bands = await db.DeliveryChargeBands.AsNoTracking()
            .Where(b => b.PlantId == plantId || b.PlantId == null)
            .ToListAsync(cancellationToken);
        var scope = bands.Any(b => b.PlantId == plantId) ? bands.Where(b => b.PlantId == plantId) : bands.Where(b => b.PlantId == null);
        var delivery = scope.FirstOrDefault(b => distanceKm > b.FromKm && (b.ToKm == null || distanceKm <= b.ToKm))?.ChargePerCft ?? 0m;

        var policy = await db.PricingPolicies.AsNoTracking().Include(p => p.AitRates).FirstOrDefaultAsync(cancellationToken);
        var vatPercent = policy?.VatPercent ?? PricingPolicy.DefaultVatPercent;
        var marginPercent = policy?.MinMarginPercent ?? PricingPolicy.DefaultMinMarginPercent;
        var aitPercent = customerType is { } type
            ? policy?.AitRates.FirstOrDefault(r => r.CustomerType == type)?.Percent ?? 0m
            : 0m;

        var materialCost = lines.Sum(l => l.Cost);
        var overhead = SumOf(OverheadKind.Overhead);
        var fuel = SumOf(OverheadKind.Fuel);
        var bridgeToll = SumOf(OverheadKind.BridgeToll);
        var totalCost = materialCost + overhead + fuel + bridgeToll + delivery;

        var share = 1m - (marginPercent + aitPercent) / 100m;
        if (share <= 0m)
        {
            return new(null, "The margin and AIT together must be less than 100%.");
        }

        // Rounded up to 2 decimals so the floor never ends up below the true cost.
        var floor = Math.Ceiling(totalCost / share * 100m) / 100m;
        var vat = Math.Round(floor * vatPercent / 100m, 2, MidpointRounding.AwayFromZero);

        return new(new FloorPriceBreakdown(
            productId, plantId, date, distanceKm, customerType, design.Id, design.Version, lines,
            materialCost, overhead, fuel, bridgeToll, delivery, totalCost,
            marginPercent, aitPercent, floor, vatPercent, vat, floor + vat), null);
    }
}