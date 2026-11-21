using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Services;

// Credit volume billed on this line at the credit's locked price.
public record InvoiceCreditUseDto(
    Guid Id, Guid CreditId, decimal QuantityCft, decimal UnitPrice, string? SourceOrderNumber, decimal Amount);

// Amount = the billed volume not covered by credit at the line's price, plus each credit use at its own price.
// CarriedForwardCft is what goes (or went) to the customer's ledger when the line carries the rest forward.
// CreditOverBilled is true when more credit is used than the line is billed for, which blocks the close.
public record InvoiceLineDto(
    Guid LineId, string ProductCode, string ProductName, decimal OrderedCft, decimal DeliveredCft,
    decimal? BilledCft, BillingBasis? Basis, string? Note, bool NeedsResolution, decimal UnitPrice, decimal Amount,
    List<InvoiceCreditUseDto> CreditUses, decimal? CarriedForwardCft, bool CreditOverBilled);

public record InvoiceAdjustmentDto(
    Guid Id, AdjustmentKind Kind, string Reason, decimal? QuantityCft, decimal Amount, DateTime CreatedAtUtc);

// Final is true once the order is closed and the figures are frozen. Before that they are provisional.
// TaxableAmount is what VAT is worked out on. It is 0 for a VAT-exempt customer.
public record InvoiceDto(
    Guid OrderId, string Number, OrderStatus Status, bool Final,
    List<InvoiceLineDto> Lines, List<OrderChargeDto> Charges, List<InvoiceAdjustmentDto> Adjustments,
    decimal ConcreteAmount, decimal ChargesAmount, decimal AdjustmentsAmount,
    decimal Subtotal, decimal TaxableAmount, bool VatExempt,
    decimal VatPercent, decimal VatAmount, decimal Total, bool CanClose);

public class OrderInvoiceCalculator(PlantFlowDbContext db)
{
    // The taxable amount is the concrete (if the company taxes it) plus the charges that carry VAT,
    // less the adjustments, and never below zero. An exempt customer has no taxable amount.
    public static decimal Taxable(
        bool vatExempt, bool vatOnConcrete, decimal concrete, IEnumerable<OrderChargeDto> charges, decimal adjustments)
    {
        if (vatExempt) return 0m;

        var taxable = (vatOnConcrete ? concrete : 0m) + charges.Where(c => c.VatApplies).Sum(c => c.Amount) - adjustments;
        return Math.Max(0m, taxable);
    }

    public static (decimal Vat, decimal Total) Totals(decimal subtotal, decimal taxable, decimal vatPercent)
    {
        var vat = Math.Round(taxable * vatPercent / 100m, 2);
        return (vat, subtotal + vat);
    }

    public async Task<InvoiceDto?> BuildAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => new
            {
                o.Number,
                o.Status,
                o.ClosedAtUtc,
                o.VatExempt,
                o.VatOnConcrete,
                o.InvoiceVatPercent,
                o.InvoiceSubtotal,
                o.InvoiceVatAmount,
                o.InvoiceTotal
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return null;

        var rows = await db.OrderLines.AsNoTracking().Where(l => l.OrderId == orderId)
            .OrderBy(l => l.Product.Code).ThenBy(l => l.PourElement)
            .Select(l => new
            {
                l.Id,
                l.Product.Code,
                l.Product.Name,
                l.QuantityCft,
                l.DeliveredCft,
                l.BilledCft,
                l.BillingBasis,
                l.BillingNote,
                l.UnitPrice
            })
            .ToListAsync(cancellationToken);

        var uses = (await db.LedgerCreditUses.AsNoTracking().Where(u => u.OrderId == orderId)
                .OrderBy(u => u.CreatedAtUtc)
                .Select(u => new
                {
                    u.Id,
                    u.OrderLineId,
                    u.CreditId,
                    u.QuantityCft,
                    u.Credit.UnitPrice,
                    SourceOrderNumber = u.Credit.SourceOrder != null ? u.Credit.SourceOrder.Number : null
                })
                .ToListAsync(cancellationToken))
            .ToLookup(u => u.OrderLineId,
                u => new InvoiceCreditUseDto(u.Id, u.CreditId, u.QuantityCft, u.UnitPrice, u.SourceOrderNumber,
                    Math.Round(u.QuantityCft * u.UnitPrice, 2)));

        var lines = rows.Select(r =>
        {
            var differs = r.DeliveredCft != r.QuantityCft;
            var effective = CustomerLedger.EffectiveBilled(r.QuantityCft, r.DeliveredCft, r.BilledCft);
            var lineUses = uses[r.Id].ToList();
            var fromCredit = lineUses.Sum(u => u.QuantityCft);
            var amount = Math.Round(Math.Max(0m, effective - fromCredit) * r.UnitPrice, 2) + lineUses.Sum(u => u.Amount);
            decimal? carried = r.BillingBasis == BillingBasis.CarriedForward ? r.QuantityCft - r.DeliveredCft : null;
            return new InvoiceLineDto(
                r.Id, r.Code, r.Name, r.QuantityCft, r.DeliveredCft, r.BilledCft, r.BillingBasis, r.BillingNote,
                differs && r.BillingBasis is null, r.UnitPrice, amount, lineUses, carried, fromCredit > effective);
        }).ToList();

        var charges = await db.OrderCharges.AsNoTracking().Where(c => c.OrderId == orderId).OrderBy(c => c.Description)
            .Select(c => new OrderChargeDto(c.Kind, c.Description, c.QuantityCft, c.RatePerCft, c.Amount, c.VatApplies))
            .ToListAsync(cancellationToken);

        var adjustments = await db.OrderAdjustments.AsNoTracking().Where(a => a.OrderId == orderId)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new InvoiceAdjustmentDto(a.Id, a.Kind, a.Reason, a.QuantityCft, a.Amount, a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var concrete = lines.Sum(l => l.Amount);
        var chargesAmount = charges.Sum(c => c.Amount);
        var adjustmentsAmount = adjustments.Sum(a => a.Amount);
        var subtotal = concrete + chargesAmount - adjustmentsAmount;
        var taxable = Taxable(order.VatExempt, order.VatOnConcrete, concrete, charges, adjustmentsAmount);

        var final = order.ClosedAtUtc is not null;
        decimal vatPercent, vat, total;

        if (final)
        {
            vatPercent = order.InvoiceVatPercent ?? 0m;
            subtotal = order.InvoiceSubtotal ?? subtotal;
            vat = order.InvoiceVatAmount ?? 0m;
            total = order.InvoiceTotal ?? subtotal + vat;
        }
        else
        {
            var policy = await db.Set<PricingPolicy>().AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            vatPercent = policy?.VatPercent ?? PricingPolicy.DefaultVatPercent;
            (vat, total) = Totals(subtotal, taxable, vatPercent);
        }

        var canClose = !final && lines.All(l => !l.NeedsResolution && !l.CreditOverBilled) && subtotal >= 0;
        return new InvoiceDto(
            orderId, order.Number, order.Status, final, lines, charges, adjustments,
            concrete, chargesAmount, adjustmentsAmount, subtotal, taxable, order.VatExempt,
            vatPercent, vat, total, canClose);
    }
}