using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;

namespace PlantFlow.Api.Services;

// What a load adds to its order line's delivered quantity, and how the line's total is kept up to date.
public static class DeliveredQuantity
{
    // A poured load counts what was poured, less what came back. A customer-fault return that was dumped is added
    // back, so the customer pays for it. A rejected load counts only that dumped part. A reused return is not billed.
    public static decimal Contribution(TruckLoad load, ReturnedConcrete? record)
    {
        if (load.CancelledAtUtc is not null) return 0m;

        var billedBack = record is { Fault: ReturnFault.Customer } ? record.DumpedCft : 0m;
        if (load.RejectedAtUtc is not null) return billedBack;
        if (load.PouringEndedAtUtc is null) return 0m;

        return load.QuantityCft - load.ReturnedCft + billedBack;
    }

    public static async Task RecalculateAsync(
        PlantFlowDbContext db, AuditWriter audit, Guid lineId, CancellationToken cancellationToken)
    {
        var line = await db.OrderLines.Include(l => l.Order).FirstAsync(l => l.Id == lineId, cancellationToken);
        if (line.Order.Status == OrderStatus.Completed) return;

        var loads = await db.TruckLoads.AsNoTracking().Where(l => l.Pour.OrderLineId == lineId).ToListAsync(cancellationToken);
        var ids = loads.Select(l => l.Id).ToList();
        var records = await db.ReturnedConcretes.AsNoTracking().Where(r => ids.Contains(r.SourceLoadId))
            .ToDictionaryAsync(r => r.SourceLoadId, cancellationToken);

        var total = loads.Sum(l => Contribution(l, records.GetValueOrDefault(l.Id)));

        if (line.DeliveredCft != total)
        {
            line.DeliveredCft = total;

            // A new quantity means the difference has to be looked at again.
            line.BilledCft = null;
            line.BillingBasis = null;
            line.BillingNote = null;
            line.BillingResolvedBy = null;
            line.BillingResolvedAtUtc = null;
        }

        if (line.Order.Status == OrderStatus.Confirmed && total > 0)
        {
            line.Order.Status = OrderStatus.InProgress;
        }

        audit.Record(line.Order.TenantId, "order.delivered_from_loads", "Order", line.Order.Id.ToString(), new { lineId, total });
        await db.SaveChangesAsync(cancellationToken);
    }
}