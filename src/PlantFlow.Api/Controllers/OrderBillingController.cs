using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public class DeliveredLineRequest
{
    [Required]
    public Guid? LineId { get; init; }

    // At most 2 decimals.
    [Range(0.0, 1000000.0)]
    public decimal DeliveredCft { get; init; }
}

public class RecordDeliveriesRequest
{
    [Required, MinLength(1)]
    public List<DeliveredLineRequest> Lines { get; init; } = [];
}

public class BillingLineRequest
{
    [Required]
    public Guid? LineId { get; init; }

    [Required]
    public BillingBasis? Basis { get; init; }

    // Only for Custom, at most 2 decimals.
    [Range(0.0, 1000000.0)]
    public decimal? BilledCft { get; init; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

public class ResolveBillingRequest
{
    [Required, MinLength(1)]
    public List<BillingLineRequest> Lines { get; init; } = [];
}

public class CreditUseRequest
{
    [Required]
    public Guid? LineId { get; init; }

    [Required]
    public Guid? CreditId { get; init; }

    // At most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal QuantityCft { get; init; }
}

// An open credit this order's customer can use on one of its products.
public record AvailableCreditDto(
    Guid CreditId, Guid ProductId, string ProductCode, decimal UnitPrice, decimal BalanceCft,
    string? SourceOrderNumber, DateTime CreatedAtUtc);

public class AdjustmentRequest
{
    [Required]
    public AdjustmentKind? Kind { get; init; }

    [Required, StringLength(200, MinimumLength = 3)]
    public string Reason { get; init; } = "";

    // Optional, at most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal? QuantityCft { get; init; }

    // The amount taken off, excluding VAT, at most 2 decimals.
    [Range(0.01, 1000000000.0)]
    public decimal Amount { get; init; }
}

[ApiController]
[Route("api/orders/{id:guid}")]
public class OrderBillingController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit,
    OrderApprovalFlow flow, OrderInvoiceCalculator invoices, CustomerLedger ledger) : ControllerBase
{
    // Open credits of this order's customer for the products on the order, oldest first.
    [HttpGet("ledger-credits")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<ActionResult<List<AvailableCreditDto>>> AvailableCredits(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new { o.CustomerId, Products = o.Lines.Select(l => l.ProductId).ToList() })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return NotFound();

        return await db.LedgerCredits.AsNoTracking()
            .Where(c => c.CustomerId == order.CustomerId && c.BalanceCft > 0 && order.Products.Contains(c.ProductId))
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new AvailableCreditDto(c.Id, c.ProductId, c.Product.Code, c.UnitPrice, c.BalanceCft,
                c.SourceOrder != null ? c.SourceOrder.Number : null, c.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    // Accounts bills part of a line from a ledger credit, at the credit's locked price.
    [HttpPost("credit-uses")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<IActionResult> UseCredit(Guid id, CreditUseRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Credit can only be used on a running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId);
        if (line is null)
        {
            return Problem(title: "That line does not belong to this order.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.QuantityCft != Math.Round(request.QuantityCft, 2))
        {
            return Problem(title: "Quantities can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        var credit = await db.LedgerCredits.FirstOrDefaultAsync(c => c.Id == request.CreditId, cancellationToken);
        if (credit is null) return NotFound();

        var refusal = await ledger.UseAsync(order, line, credit, request.QuantityCft, cancellationToken);
        if (refusal is not null) return Problem(title: refusal, statusCode: StatusCodes.Status409Conflict);

        return await SaveLedgerAsync(cancellationToken) ?? NoContent();
    }

    [HttpDelete("credit-uses/{useId:guid}")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<IActionResult> RemoveCreditUse(Guid id, Guid useId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        var use = await db.LedgerCreditUses.Include(u => u.Credit)
            .FirstOrDefaultAsync(u => u.Id == useId && u.OrderId == id, cancellationToken);
        if (use is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Credit can only be removed from a running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        ledger.RemoveUse(order, use);
        return await SaveLedgerAsync(cancellationToken) ?? NoContent();
    }

    private async Task<IActionResult?> SaveLedgerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(title: CustomerLedger.ChangedMessage, statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpGet("invoice")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<ActionResult<InvoiceDto>> Invoice(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoices.BuildAsync(id, cancellationToken);
        if (invoice is null) return NotFound();
        return invoice;
    }

    // Records what was delivered per line. A changed quantity clears that line's billing resolution.
    [HttpPut("deliveries")]
    [HasPermission(Permissions.OrdersClose)]
    public async Task<IActionResult> RecordDeliveries(Guid id, RecordDeliveriesRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProgress))
        {
            return Problem(title: $"Deliveries can only be recorded on a confirmed or running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var byId = order.Lines.ToDictionary(l => l.Id);
        var ids = request.Lines.Select(l => l.LineId!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count || ids.Any(i => !byId.ContainsKey(i)))
        {
            return Problem(title: "Each line id must belong to this order and appear only once.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Lines.Any(l => l.DeliveredCft != Math.Round(l.DeliveredCft, 2)))
        {
            return Problem(title: "Quantities can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        var log = new List<object>();
        foreach (var r in request.Lines)
        {
            var line = byId[r.LineId!.Value];
            if (line.DeliveredCft == r.DeliveredCft) continue;

            log.Add(new { Line = line.Id, Before = line.DeliveredCft, After = r.DeliveredCft });
            line.DeliveredCft = r.DeliveredCft;
            ClearResolution(line);
        }

        if (order.Status == OrderStatus.Confirmed && order.Lines.Any(l => l.DeliveredCft > 0))
        {
            flow.ChangeStatus(order, OrderStatus.InProgress, "Delivery recorded");
        }

        audit.Record(order.TenantId, "order.deliveries_recorded", "Order", order.Id.ToString(), new { order.Number, Lines = log });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Accounts decides what is billed where delivered and ordered quantities differ.
    [HttpPut("billing")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<IActionResult> ResolveBilling(Guid id, ResolveBillingRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Billing can only be resolved on a running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var byId = order.Lines.ToDictionary(l => l.Id);
        var ids = request.Lines.Select(l => l.LineId!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count || ids.Any(i => !byId.ContainsKey(i)))
        {
            return Problem(title: "Each line id must belong to this order and appear only once.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        foreach (var r in request.Lines)
        {
            if (r.Basis == BillingBasis.Custom)
            {
                if (r.BilledCft is null || r.BilledCft != Math.Round(r.BilledCft.Value, 2))
                {
                    return Problem(title: "A custom billing needs a quantity with at most 2 decimal places.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }
            else if (r.BilledCft is not null)
            {
                return Problem(title: "Only a custom billing takes a quantity.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (r.Basis == BillingBasis.CarriedForward && byId[r.LineId!.Value].DeliveredCft >= byId[r.LineId!.Value].QuantityCft)
            {
                return Problem(title: "Only a line delivered short of what was ordered can carry the rest forward.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        var log = new List<object>();
        foreach (var r in request.Lines)
        {
            var line = byId[r.LineId!.Value];
            var billed = r.Basis switch
            {
                BillingBasis.Delivered or BillingBasis.CarriedForward => line.DeliveredCft,
                BillingBasis.Ordered => line.QuantityCft,
                _ => r.BilledCft!.Value
            };

            log.Add(new { Line = line.Id, Basis = r.Basis.ToString(), Billed = billed, Reason = r.Reason.Trim() });
            line.BilledCft = billed;
            line.BillingBasis = r.Basis;
            line.BillingNote = r.Reason.Trim();
            line.BillingResolvedBy = currentUser.UserId;
            line.BillingResolvedAtUtc = DateTime.UtcNow;
        }

        audit.Record(order.TenantId, "order.billing_resolved", "Order", order.Id.ToString(), new { order.Number, Lines = log });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("adjustments")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<IActionResult> AddAdjustment(Guid id, AdjustmentRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Adjustments can only be added to a running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (request.Amount != Math.Round(request.Amount, 2)
            || (request.QuantityCft is { } qty && qty != Math.Round(qty, 2)))
        {
            return Problem(title: "Amounts and quantities can have at most 2 decimal places.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var adjustment = new OrderAdjustment
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            Kind = request.Kind!.Value,
            Reason = request.Reason.Trim(),
            QuantityCft = request.QuantityCft,
            Amount = request.Amount,
            CreatedBy = currentUser.UserId
        };
        db.OrderAdjustments.Add(adjustment);

        audit.Record(order.TenantId, "order.adjustment_added", "Order", order.Id.ToString(),
            new { order.Number, Kind = adjustment.Kind.ToString(), adjustment.Reason, adjustment.QuantityCft, adjustment.Amount });
        await db.SaveChangesAsync(cancellationToken);

        var dto = new InvoiceAdjustmentDto(
            adjustment.Id, adjustment.Kind, adjustment.Reason, adjustment.QuantityCft, adjustment.Amount, adjustment.CreatedAtUtc);
        return Created($"api/orders/{id}/invoice", dto);
    }

    [HttpDelete("adjustments/{adjustmentId:guid}")]
    [HasPermission(Permissions.OrdersBillingResolve)]
    public async Task<IActionResult> RemoveAdjustment(Guid id, Guid adjustmentId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        var adjustment = await db.OrderAdjustments.FirstOrDefaultAsync(
            a => a.Id == adjustmentId && a.OrderId == id, cancellationToken);
        if (adjustment is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Adjustments can only be removed from a running order. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.OrderAdjustments.Remove(adjustment);
        audit.Record(order.TenantId, "order.adjustment_removed", "Order", order.Id.ToString(),
            new { order.Number, Kind = adjustment.Kind.ToString(), adjustment.Reason, adjustment.Amount });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Freezes the invoice figures and completes the order. Every difference must be resolved first.
    [HttpPost("close")]
    [HasPermission(Permissions.OrdersClose)]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.InProgress)
        {
            return Problem(title: $"Only a running order can be closed. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var invoice = (await invoices.BuildAsync(id, cancellationToken))!;

        var unresolved = invoice.Lines.Where(l => l.NeedsResolution).Select(l => l.ProductCode).Distinct().ToList();
        if (unresolved.Count > 0)
        {
            return Problem(title: "Resolve the billed quantity where delivered and ordered differ: " + string.Join(", ", unresolved) + ".",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (invoice.Subtotal < 0)
        {
            return Problem(title: "The adjustments are larger than the amount billed.", statusCode: StatusCodes.Status409Conflict);
        }

        var overUsed = await ledger.OverUsedLinesAsync(order, cancellationToken);
        if (overUsed.Count > 0)
        {
            return Problem(title: "More credit is used than these lines are billed for: " + string.Join(", ", overUsed) +
                                  ". Remove some of the credit first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        foreach (var line in order.Lines.Where(l => l.BilledCft is null))
        {
            line.BilledCft = line.QuantityCft;
        }

        ledger.CarryForward(order);

        order.ClosedBy = currentUser.UserId;
        order.ClosedAtUtc = DateTime.UtcNow;
        order.InvoiceVatPercent = invoice.VatPercent;
        order.InvoiceSubtotal = invoice.Subtotal;
        order.InvoiceVatAmount = invoice.VatAmount;
        order.InvoiceTotal = invoice.Total;
        flow.ChangeStatus(order, OrderStatus.Completed, null);

        audit.Record(order.TenantId, "order.closed", "Order", order.Id.ToString(),
            new { order.Number, invoice.Subtotal, invoice.VatPercent, invoice.VatAmount, invoice.Total });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static void ClearResolution(OrderLine line)
    {
        line.BilledCft = null;
        line.BillingBasis = null;
        line.BillingNote = null;
        line.BillingResolvedBy = null;
        line.BillingResolvedAtUtc = null;
    }
}