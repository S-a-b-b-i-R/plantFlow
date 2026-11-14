using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Services;

public record InvoiceLineDto(
    Guid LineId, string ProductCode, string ProductName, decimal OrderedCft, decimal DeliveredCft,
    decimal? BilledCft, BillingBasis? Basis, string? Note, bool NeedsResolution, decimal UnitPrice, decimal Amount);

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

        var lines = rows.Select(r =>
        {
            var differs = r.DeliveredCft != r.QuantityCft;
            var effective = r.BilledCft ?? (differs ? r.DeliveredCft : r.QuantityCft);
            return new InvoiceLineDto(
                r.Id, r.Code, r.Name, r.QuantityCft, r.DeliveredCft, r.BilledCft, r.BillingBasis, r.BillingNote,
                differs && r.BillingBasis is null, r.UnitPrice, Math.Round(effective * r.UnitPrice, 2));
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

        var canClose = !final && lines.All(l => !l.NeedsResolution) && subtotal >= 0;
        return new InvoiceDto(
            orderId, order.Number, order.Status, final, lines, charges, adjustments,
            concrete, chargesAmount, adjustmentsAmount, subtotal, taxable, order.VatExempt,
            vatPercent, vat, total, canClose);
    }
}