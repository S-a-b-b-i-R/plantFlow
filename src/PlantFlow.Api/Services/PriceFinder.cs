using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Pricing;

namespace PlantFlow.Api.Services;

public enum PriceScope { CustomerAndPlant, Customer, Plant, General }

public record PriceMatch(Guid PriceId, decimal PricePerCft, PriceScope Scope, DateOnly ValidFrom, DateOnly? ValidTo);

// Finds the active price that applies. The most specific one wins.
public class PriceFinder(PlantFlowDbContext db)
{
    public async Task<PriceMatch?> FindAsync(
        Guid productId, Guid plantId, Guid? customerId, DateOnly date, CancellationToken cancellationToken)
    {
        var candidates = await db.Prices.AsNoTracking()
            .Where(p => p.ProductId == productId && p.Status == PriceStatus.Active
                        && p.ValidFrom <= date && (p.ValidTo == null || p.ValidTo >= date)
                        && (p.PlantId == plantId || p.PlantId == null)
                        && (p.CustomerId == null || p.CustomerId == customerId))
            .ToListAsync(cancellationToken);

        // Overlaps are refused when a price is approved, so each scope has at most one candidate.
        return candidates
            .Select(p => new PriceMatch(p.Id, p.PricePerCft, ScopeOf(p), p.ValidFrom, p.ValidTo))
            .OrderBy(m => m.Scope)
            .FirstOrDefault();
    }

    private static PriceScope ScopeOf(Price p) => (p.CustomerId, p.PlantId) switch
    {
        (not null, not null) => PriceScope.CustomerAndPlant,
        (not null, null) => PriceScope.Customer,
        (null, not null) => PriceScope.Plant,
        _ => PriceScope.General
    };
}