using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Services;

// Works out the charges on top of the concrete. Today that is the pump charge, per CFT pumped.
public class OrderChargeCalculator(PlantFlowDbContext db)
{
    // Replaces the order's charges with freshly calculated ones. The order must be loaded with its Charges.
    // Returns an error message when the setup stops a charge from being calculated.
    public async Task<string?> ReplaceAsync(Order order, IEnumerable<OrderLine> lines, CancellationToken cancellationToken)
    {
        var pumped = lines.Where(l => l.PourMethod == PourMethod.Pump).Sum(l => l.QuantityCft);
        var charge = pumped > 0 ? await PumpChargeAsync(order, pumped, cancellationToken) : null;

        if (charge?.Error is not null) return charge.Error;

        db.OrderCharges.RemoveRange(order.Charges);
        if (charge?.Charge is { } added) db.OrderCharges.Add(added);
        return null;
    }

    private sealed record PumpResult(OrderCharge? Charge, string? Error);

    private async Task<PumpResult> PumpChargeAsync(Order order, decimal pumped, CancellationToken cancellationToken)
    {
        var freeAbove = await db.Tenants.Where(t => t.Id == order.TenantId)
            .Select(t => t.PumpFreeAboveCft).FirstAsync(cancellationToken);
        if (freeAbove is not null && pumped > freeAbove) return new PumpResult(null, null);

        var pump = await db.ServiceCharges
            .FirstOrDefaultAsync(s => s.Kind == ServiceChargeKind.Pump && s.Status == ActiveStatus.Active, cancellationToken);
        if (pump is null || pump.DefaultAmount <= 0) return new PumpResult(null, null);

        if (pump.Basis != ChargeBasis.PerCft)
        {
            return new PumpResult(null, "The pump charge must be set up per CFT before an order with pumping can be confirmed.");
        }

        return new PumpResult(new OrderCharge
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            Kind = ServiceChargeKind.Pump,
            ServiceChargeId = pump.Id,
            Description = $"Pump charge: {pumped:0.##} CFT pumped",
            QuantityCft = pumped,
            RatePerCft = pump.DefaultAmount,
            Amount = Math.Round(pumped * pump.DefaultAmount, 2),
            VatApplies = pump.VatApplies
        }, null);
    }

    // Freezes the tax terms the invoice will use: the customer's exemption and the company's VAT-on-concrete setting.
    public async Task SnapshotTaxTermsAsync(Order order, CancellationToken cancellationToken)
    {
        order.VatExempt = await db.Customers.Where(c => c.Id == order.CustomerId)
            .Select(c => c.VatExempt).FirstAsync(cancellationToken);
        order.VatOnConcrete = await db.Tenants.Where(t => t.Id == order.TenantId)
            .Select(t => t.VatOnConcrete).FirstAsync(cancellationToken);
    }
}