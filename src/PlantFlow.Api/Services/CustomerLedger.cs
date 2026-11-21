using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Ledger;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Services;

// The customer volume ledger: credits carried forward from short-closed lines, used on later orders of the
// same customer and product at the locked price. Every change keeps LedgerCredit.BalanceCft up to date.
public class CustomerLedger(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit)
{
    public const string ChangedMessage = "The ledger changed while you were saving. Reload and try again.";

    // What a line is billed for right now: the decided quantity, or until then delivered when it differs
    // from ordered, otherwise ordered. OrderInvoiceCalculator uses the same rule.
    public static decimal EffectiveBilled(decimal ordered, decimal delivered, decimal? billed) =>
        billed ?? (delivered != ordered ? delivered : ordered);

    // Bills part of a line from a credit. Returns a refusal message, or null when it was added.
    public async Task<string?> UseAsync(Order order, OrderLine line, LedgerCredit credit, decimal quantity,
        CancellationToken cancellationToken)
    {
        if (credit.CustomerId != order.CustomerId)
            return "That credit belongs to another customer.";
        if (credit.ProductId != line.ProductId)
            return "A credit can only be used on the same product it was carried forward for.";
        if (quantity > credit.BalanceCft)
            return $"Only {credit.BalanceCft:0.##} CFT of that credit is left to use.";

        var alreadyUsed = await db.LedgerCreditUses.Where(u => u.OrderLineId == line.Id)
            .SumAsync(u => (decimal?)u.QuantityCft, cancellationToken) ?? 0m;
        var billed = EffectiveBilled(line.QuantityCft, line.DeliveredCft, line.BilledCft);
        if (alreadyUsed + quantity > billed)
        {
            return $"This line is billed for {billed:0.##} CFT and {alreadyUsed:0.##} CFT of it already comes from credits. " +
                   "Use less credit.";
        }

        var use = new LedgerCreditUse
        {
            TenantId = order.TenantId,
            CreditId = credit.Id,
            OrderId = order.Id,
            OrderLineId = line.Id,
            QuantityCft = quantity,
            CreatedBy = currentUser.UserId
        };
        db.LedgerCreditUses.Add(use);
        credit.BalanceCft -= quantity;

        audit.Record(order.TenantId, "ledger.credit_used", "LedgerCredit", credit.Id.ToString(),
            new { Order = order.Number, LineId = line.Id, QuantityCft = quantity, credit.UnitPrice, credit.BalanceCft });
        return null;
    }

    public void RemoveUse(Order order, LedgerCreditUse use)
    {
        use.Credit.BalanceCft += use.QuantityCft;
        db.LedgerCreditUses.Remove(use);
        audit.Record(order.TenantId, "ledger.credit_use_removed", "LedgerCredit", use.CreditId.ToString(),
            new { Order = order.Number, LineId = use.OrderLineId, use.QuantityCft, use.Credit.BalanceCft });
    }

    // A cancelled order gives back the credit it was using.
    public async Task ReleaseUsesAsync(Order order, CancellationToken cancellationToken)
    {
        var uses = await db.LedgerCreditUses.Include(u => u.Credit).Where(u => u.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        foreach (var use in uses) RemoveUse(order, use);
    }

    // Lines that would be billed for less than the credit used on them, for example after deliveries changed.
    public async Task<List<string>> OverUsedLinesAsync(Order order, CancellationToken cancellationToken)
    {
        var used = await db.LedgerCreditUses.Where(u => u.OrderId == order.Id)
            .GroupBy(u => u.OrderLineId)
            .Select(g => new { LineId = g.Key, Cft = g.Sum(u => u.QuantityCft) })
            .ToDictionaryAsync(x => x.LineId, x => x.Cft, cancellationToken);

        var codes = await db.OrderLines.Where(l => l.OrderId == order.Id)
            .Select(l => new { l.Id, l.Product.Code }).ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

        return order.Lines
            .Where(l => used.TryGetValue(l.Id, out var cft) && cft > EffectiveBilled(l.QuantityCft, l.DeliveredCft, l.BilledCft))
            .Select(l => codes[l.Id]).Distinct().ToList();
    }

    // At close: each carried-forward line puts what was not delivered into the ledger at the line's price.
    public void CarryForward(Order order)
    {
        foreach (var line in order.Lines.Where(l => l.BillingBasis == BillingBasis.CarriedForward))
        {
            var cft = line.QuantityCft - line.DeliveredCft;
            if (cft <= 0) continue;

            var credit = new LedgerCredit
            {
                TenantId = order.TenantId,
                CustomerId = order.CustomerId,
                ProductId = line.ProductId,
                UnitPrice = line.UnitPrice,
                Source = LedgerCreditSource.CarriedForward,
                SourceOrderId = order.Id,
                SourceOrderLineId = line.Id,
                OriginalCft = cft,
                BalanceCft = cft,
                Note = line.BillingNote,
                CreatedBy = currentUser.UserId
            };
            db.LedgerCredits.Add(credit);
            audit.Record(order.TenantId, "ledger.credit_carried_forward", "LedgerCredit", credit.Id.ToString(),
                new { Order = order.Number, LineId = line.Id, credit.ProductId, QuantityCft = cft, credit.UnitPrice });
        }
    }
}
