using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Pricing;

namespace PlantFlow.Api.Services;

public record SetupStepDto(string Key, bool Done);

public record SetupProgressDto(List<SetupStepDto> Steps, int Done, int Total);

// What a new company still has to set up before it can run: one "at least one of these" check per step.
// It only guides; the rules that refuse an order without a price and so on live where they always did.
// Queries ignore the tenant filter and name the tenant, so the platform console can ask for any company.
public class SetupChecklist(PlantFlowDbContext db)
{
    public async Task<SetupProgressDto> ForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var steps = new List<SetupStepDto>
        {
            // The time zone locks once there are orders, so the settings should be looked at first.
            new("settings", await db.AuditLogs.IgnoreQueryFilters()
                .AnyAsync(a => a.TenantId == tenantId && a.Action == "tenant.settings_changed", cancellationToken)),
            new("plant", await db.Plants.IgnoreQueryFilters()
                .AnyAsync(p => p.TenantId == tenantId && p.Status == ActiveStatus.Active
                    && db.PlantShifts.Any(s => s.PlantId == p.Id && s.Status == ActiveStatus.Active)
                    && db.BatchingPlants.Any(b => b.PlantId == p.Id && b.Status == ActiveStatus.Active), cancellationToken)),
            new("fleet", await db.Vehicles.IgnoreQueryFilters()
                    .AnyAsync(v => v.TenantId == tenantId && v.Type == VehicleType.TransitMixer
                                   && v.Status != VehicleStatus.Retired, cancellationToken)
                && await db.Drivers.IgnoreQueryFilters()
                    .AnyAsync(d => d.TenantId == tenantId && d.Status == ActiveStatus.Active, cancellationToken)),
            new("catalogue", await db.Products.IgnoreQueryFilters()
                    .AnyAsync(p => p.TenantId == tenantId && p.Status == ActiveStatus.Active, cancellationToken)
                && await db.Materials.IgnoreQueryFilters()
                    .AnyAsync(m => m.TenantId == tenantId && m.Status == ActiveStatus.Active, cancellationToken)),
            new("recipe", await db.MixDesigns.IgnoreQueryFilters()
                .AnyAsync(m => m.TenantId == tenantId && m.Status == MixDesignStatus.Approved, cancellationToken)),
            new("costs", await db.MaterialCosts.IgnoreQueryFilters()
                .AnyAsync(c => c.TenantId == tenantId && c.Status == CostStatus.Approved, cancellationToken)),
            new("prices", await db.Prices.IgnoreQueryFilters()
                .AnyAsync(p => p.TenantId == tenantId && p.Status == PriceStatus.Active, cancellationToken)),
            new("sales-rep", await db.SalesReps.IgnoreQueryFilters()
                .AnyAsync(r => r.TenantId == tenantId && r.Status == ActiveStatus.Active && r.UserId != null, cancellationToken)),
            new("customer", await db.Sites.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenantId && s.Status == ActiveStatus.Active
                    && db.Customers.Any(c => c.Id == s.CustomerId && c.Status == ActiveStatus.Active), cancellationToken)),
            new("people", await db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == tenantId && u.Status == UserStatus.Active, cancellationToken) >= 2),
        };
        return new SetupProgressDto(steps, steps.Count(s => s.Done), steps.Count);
    }
}
