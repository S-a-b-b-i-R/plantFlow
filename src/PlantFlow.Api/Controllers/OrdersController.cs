using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record OrderSummaryDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, Guid SiteId, string SiteName,
    Guid? SalesRepId, string? SalesRepName, Guid PlantId, string PlantCode, DateOnly RequestedDate,
    decimal DistanceKm, OrderStatus Status, string? PaymentTerms, string? Notes, string? CancelReason,
    int LineCount, decimal TotalQuantityCft, decimal TotalValue, int BelowFloorLineCount, DateTime? ConfirmedAtUtc, Guid? ConfirmedBy, ConfirmationMethod? ConfirmationMethod,
    string? ConfirmationReference, string? HoldReason);

public record OrderLineDto(
    Guid Id, Guid ProductId, string ProductCode, string ProductName, Guid? MixDesignId,
    decimal QuantityCft, decimal UnitPrice, Guid? PriceId, decimal? ListPrice, decimal? FloorPrice, bool BelowFloor,
    int SlumpMm, PourElement PourElement, PourMethod PourMethod, string? Notes, decimal LineValue);

public record OrderHistoryDto(OrderStatus? FromStatus, OrderStatus ToStatus, DateTime ChangedAtUtc, Guid? ChangedBy, string? Reason);

public record OrderApprovalDto(
    ApprovalStage Stage, ApprovalDecision Decision, int Round, DateTime OpenedAtUtc,
    Guid? DecidedBy, DateTime? DecidedAtUtc, string? Comment);

public record OrderChargeDto(ServiceChargeKind Kind, string Description, decimal QuantityCft, decimal RatePerCft, decimal Amount);

public record OrderDetailDto(
    OrderSummaryDto Order, List<OrderLineDto> Lines, List<OrderHistoryDto> History, List<OrderApprovalDto> Approvals,
    List<OrderChargeDto> Charges);

public class OrderLineRequest
{
    // Send the id of an existing line to keep and update it. Leave it out for a new line.
    public Guid? Id { get; init; }

    [Required]
    public Guid? ProductId { get; init; }

    // At most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal QuantityCft { get; init; }

    // Per CFT, excluding VAT, at most 2 decimals. Leave it out to use the active price.
    [Range(0.01, 1000000.0)]
    public decimal? UnitPrice { get; init; }

    // Leave it out to use the product's default slump.
    [Range(0, 300)]
    public int? SlumpMm { get; init; }

    [Required]
    public PourElement? PourElement { get; init; }

    [Required]
    public PourMethod? PourMethod { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public class OrderRequest
{
    [Required]
    public Guid? CustomerId { get; init; }

    // Must belong to the customer.
    [Required]
    public Guid? SiteId { get; init; }

    // Leave it out to use the customer's own rep.
    public Guid? SalesRepId { get; init; }

    [Required]
    public Guid? PlantId { get; init; }

    [Required]
    public DateOnly? RequestedDate { get; init; }

    // Km from plant to site, at most 2 decimals. Leave it out to use the distance stored on the site.
    [Range(0.0, 10000.0)]
    public decimal? DistanceKm { get; init; }

    [StringLength(100)]
    public string? PaymentTerms { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [Required, MinLength(1)]
    public List<OrderLineRequest> Lines { get; init; } = [];
}

public class ReviseOrderRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";

    // The whole order as it should be, in the same shape as an update.
    [Required]
    public OrderRequest Order { get; init; } = null!;
}

public class CancelOrderRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
[Route("api/orders")]
public class OrdersController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit,
    FloorPriceCalculator calculator, PriceFinder finder, OrderApprovalFlow flow, OrderChargeCalculator chargeCalculator) : ControllerBase
{
    private static readonly Expression<Func<Order, OrderSummaryDto>> ToSummary = o => new OrderSummaryDto(
        o.Id, o.Number, o.CustomerId, o.Customer.Name, o.SiteId, o.Site.Name,
        o.SalesRepId, o.SalesRep != null ? o.SalesRep.Name : null, o.PlantId, o.Plant.Code, o.RequestedDate,
        o.DistanceKm, o.Status, o.PaymentTerms, o.Notes, o.CancelReason,
        o.Lines.Count(), o.Lines.Sum(l => l.QuantityCft), o.Lines.Sum(l => l.QuantityCft * l.UnitPrice),
        o.Lines.Count(l => l.FloorPrice != null && l.UnitPrice < l.FloorPrice),
        o.ConfirmedAtUtc, o.ConfirmedBy, o.ConfirmationMethod, o.ConfirmationReference, o.HoldReason);

    private static readonly Expression<Func<OrderLine, OrderLineDto>> ToLine = l => new OrderLineDto(
        l.Id, l.ProductId, l.Product.Code, l.Product.Name, l.MixDesignId,
        l.QuantityCft, l.UnitPrice, l.PriceId, l.Price != null ? l.Price.PricePerCft : (decimal?)null,
        l.FloorPrice, l.FloorPrice != null && l.UnitPrice < l.FloorPrice,
        l.SlumpMm, l.PourElement, l.PourMethod, l.Notes, l.QuantityCft * l.UnitPrice);

    // ?search= matches number, customer or site.  ?status= ?customerId= ?siteId= ?plantId= ?salesRepId=
    // ?requestedFrom= ?requestedTo= (dates).  ?awaitingStage=SalesManager|Management|Credit (an approval is waiting).
    // ?sort=date|-date. The default is newest first.
    [HttpGet]
    [HasPermission(Permissions.OrdersView)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] OrderStatus? status, [FromQuery] Guid? customerId,
        [FromQuery] Guid? siteId, [FromQuery] Guid? plantId, [FromQuery] Guid? salesRepId,
        [FromQuery] DateOnly? requestedFrom, [FromQuery] DateOnly? requestedTo,
        [FromQuery] ApprovalStage? awaitingStage, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking();

        if (status is not null) query = query.Where(o => o.Status == status);
        if (customerId is not null) query = query.Where(o => o.CustomerId == customerId);
        if (siteId is not null) query = query.Where(o => o.SiteId == siteId);
        if (plantId is not null) query = query.Where(o => o.PlantId == plantId);
        if (salesRepId is not null) query = query.Where(o => o.SalesRepId == salesRepId);
        if (requestedFrom is not null) query = query.Where(o => o.RequestedDate >= requestedFrom);
        if (requestedTo is not null) query = query.Where(o => o.RequestedDate <= requestedTo);

        if (awaitingStage is not null)
        {
            query = query.Where(o => o.Approvals.Any(a =>
                a.Round == o.ApprovalRound && a.Stage == awaitingStage && a.Decision == ApprovalDecision.Pending));
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(o => o.Number.Contains(search) || o.Customer.Name.Contains(search) || o.Site.Name.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "date" => query.OrderBy(o => o.RequestedDate).ThenBy(o => o.Sequence),
            "-date" => query.OrderByDescending(o => o.RequestedDate).ThenByDescending(o => o.Sequence),
            _ => query.OrderByDescending(o => o.Sequence)
        };

        return await query.Select(ToSummary).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(id, cancellationToken);
        if (detail is null) return NotFound();
        return detail;
    }

    [HttpPost]
    [HasPermission(Permissions.OrdersCreate)]
    public async Task<ActionResult<OrderDetailDto>> Create(OrderRequest request, CancellationToken cancellationToken)
    {
        var (prepared, problem) = await PrepareAsync(request, null, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var sequence = (await db.Orders.MaxAsync(o => (int?)o.Sequence, cancellationToken) ?? 0) + 1;

        var order = new Order
        {
            TenantId = tenantId,
            Sequence = sequence,
            Number = $"SO-{sequence:D6}",
            CustomerId = request.CustomerId!.Value,
            SiteId = request.SiteId!.Value,
            SalesRepId = prepared!.SalesRepId,
            PlantId = request.PlantId!.Value,
            RequestedDate = request.RequestedDate!.Value,
            DistanceKm = prepared.DistanceKm,
            PaymentTerms = request.PaymentTerms?.Trim(),
            Notes = request.Notes?.Trim(),
            Lines = request.Lines.Select((r, i) => NewLine(tenantId, r, prepared.Lines[i])).ToList()
        };
        order.History.Add(HistoryEntry(order, null, OrderStatus.Draft, null));

        db.Orders.Add(order);
        audit.Record(tenantId, "order.created", "Order", order.Id.ToString(),
            new { order.Number, order.CustomerId, order.SiteId, order.PlantId, order.DistanceKm, Lines = order.Lines.Count });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two orders took the same number at the same moment.
            return Problem(title: "Another order was created at the same moment. Please try again.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var detail = await LoadDetailAsync(order.Id, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, detail);
    }

    // Only a draft can be edited. The lines are replaced as described on OrderLineRequest.
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.OrdersEdit)]
    public async Task<IActionResult> Update(Guid id, OrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Draft)
        {
            return Problem(title: $"Only a draft can be edited. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var (prepared, problem) = await PrepareAsync(request, order, cancellationToken);
        if (problem is not null) return problem;

        var changes = Compare(order, request, prepared!);
        Apply(order, request, prepared!);

        audit.Record(order.TenantId, "order.updated", "Order", order.Id.ToString(), new { order.Number, changes.Log });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // The Sales Manager changes the BOQ while the order is waiting for approval. Every change is logged.
    // A changed price, quantity, line, distance or date restarts the Management and Credit approvals.
    [HttpPut("{id:guid}/revise")]
    [HasPermission(Permissions.OrdersApproveSalesManager)]
    public async Task<IActionResult> Revise(Guid id, ReviseOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status is not (OrderStatus.Submitted or OrderStatus.InApproval))
        {
            return Problem(title: $"Only an order waiting for approval can be revised. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var (prepared, problem) = await PrepareAsync(request.Order, order, cancellationToken);
        if (problem is not null) return problem;

        var changes = Compare(order, request.Order, prepared!);
        var lines = Apply(order, request.Order, prepared!);

        if (changes.Commercial) flow.WithdrawCommercialStages(order);
        await flow.AdvanceAsync(order, lines, cancellationToken);

        audit.Record(order.TenantId, "order.revised", "Order", order.Id.ToString(),
            new { order.Number, Reason = request.Reason.Trim(), changes.Commercial, changes.Log });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // The Sales Manager changes a confirmed order. A below-floor line with a commercial change, or a higher
    // total than the one approved, sends it back to Management (and Credit) before it is Confirmed again.
    [HttpPut("{id:guid}/amend")]
    [HasPermission(Permissions.OrdersApproveSalesManager)]
    public async Task<IActionResult> Amend(Guid id, ReviseOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).Include(o => o.Approvals).Include(o => o.Charges)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Confirmed)
        {
            return Problem(title: $"Only a confirmed order can be amended. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var (prepared, problem) = await PrepareAsync(request.Order, order, cancellationToken);
        if (problem is not null) return problem;

        var changes = Compare(order, request.Order, prepared!);
        var lines = Apply(order, request.Order, prepared!);

        var chargeError = await chargeCalculator.ReplaceAsync(order, lines, cancellationToken);
        if (chargeError is not null)
        {
            return Problem(title: chargeError, statusCode: StatusCodes.Status409Conflict);
        }

        var newTotal = lines.Sum(l => l.QuantityCft * l.UnitPrice);
        var higher = newTotal > (order.ConfirmedTotal ?? 0m);
        var reopen = higher || (changes.Commercial && lines.Any(OrderApprovalFlow.IsBelowFloor));

        order.ConfirmedTotal = newTotal;
        if (reopen)
        {
            order.ForceManagement = higher;
            flow.WithdrawCommercialStages(order);
            await flow.AdvanceAsync(order, lines, cancellationToken);
        }

        audit.Record(order.TenantId, "order.amended", "Order", order.Id.ToString(),
            new { order.Number, Reason = request.Reason.Trim(), changes.Commercial, ApprovalAgain = reopen, NewTotal = newTotal, changes.Log });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Recalculates every line's floor price with today's costs. A line whose floor cannot be calculated blocks the submit.
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.OrdersEdit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Draft)
        {
            return Problem(title: $"Only a draft can be submitted. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var customerType = await db.Customers.Where(c => c.Id == order.CustomerId).Select(c => c.Type)
            .FirstAsync(cancellationToken);

        var blockers = new List<string>();
        foreach (var line in order.Lines)
        {
            var floor = await calculator.CalculateAsync(
                line.ProductId, order.PlantId, order.RequestedDate, order.DistanceKm, customerType, cancellationToken);

            line.FloorPrice = floor.Breakdown?.FloorPrice;
            line.MixDesignId = floor.Breakdown?.MixDesignId;

            if (floor.Breakdown is null) blockers.Add($"{line.Product.Code}: {floor.Error}");
        }

        if (blockers.Count > 0)
        {
            return Problem(title: "The floor price cannot be calculated for every line. " + string.Join(" ", blockers),
                statusCode: StatusCodes.Status409Conflict);
        }

        flow.Start(order);

        audit.Record(order.TenantId, "order.submitted", "Order", order.Id.ToString(),
            new { order.Number, order.ApprovalRound, BelowFloorLines = order.Lines.Count(OrderApprovalFlow.IsBelowFloor) });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Allowed until the first truck leaves. Any approval still waiting is withdrawn.
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.OrdersEdit)]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Approvals).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status is not (OrderStatus.Draft or OrderStatus.Submitted or OrderStatus.InApproval
                         or OrderStatus.Approved or OrderStatus.Confirmed or OrderStatus.OnHold))
        {
            return Problem(title: $"An order that is {order.Status} cannot be cancelled here.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var from = order.Status;
        order.CancelReason = request.Reason.Trim();
        flow.WithdrawPending(order);
        flow.ChangeStatus(order, OrderStatus.Cancelled, order.CancelReason);

        audit.Record(order.TenantId, "order.cancelled", "Order", order.Id.ToString(),
            new { order.Number, From = from.ToString(), order.CancelReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private record LineEval(decimal UnitPrice, Guid? PriceId, decimal? FloorPrice, Guid? MixDesignId, int SlumpMm);

    private record Prepared(Guid? SalesRepId, decimal DistanceKm, List<LineEval> Lines);

    private sealed record Changes(bool Commercial, object Log);

    // Checks the request and works out the distance, unit prices and floor prices. Nothing is saved here.
    // Customers, sites, plants, reps and products are only checked for being active when they changed.
    private async Task<(Prepared? Prepared, ObjectResult? Problem)> PrepareAsync(
        OrderRequest request, Order? existing, CancellationToken cancellationToken)
    {
        ObjectResult Bad(string title) => Problem(title: title, statusCode: StatusCodes.Status400BadRequest);

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == request.CustomerId)
            .Select(c => new { c.Type, c.SalesRepId, c.Status }).FirstOrDefaultAsync(cancellationToken);
        if (customer is null || (existing?.CustomerId != request.CustomerId && customer.Status != ActiveStatus.Active))
        {
            return (null, Bad("Choose an active customer of this company."));
        }

        var siteStatus = await db.Sites.AsNoTracking()
            .Where(s => s.Id == request.SiteId && s.CustomerId == request.CustomerId)
            .Select(s => (ActiveStatus?)s.Status).FirstOrDefaultAsync(cancellationToken);
        if (siteStatus is null || (existing?.SiteId != request.SiteId && siteStatus != ActiveStatus.Active))
        {
            return (null, Bad("Choose an active site that belongs to this customer."));
        }

        var plantStatus = await db.Plants.AsNoTracking().Where(p => p.Id == request.PlantId)
            .Select(p => (ActiveStatus?)p.Status).FirstOrDefaultAsync(cancellationToken);
        if (plantStatus is null || (existing?.PlantId != request.PlantId && plantStatus != ActiveStatus.Active))
        {
            return (null, Bad("Choose an active plant of this company."));
        }

        if (request.SalesRepId is { } repId && repId != existing?.SalesRepId
            && !await db.SalesReps.AnyAsync(r => r.Id == repId && r.Status == ActiveStatus.Active, cancellationToken))
        {
            return (null, Bad("Choose an active sales rep of this company."));
        }

        var distance = request.DistanceKm
            ?? await db.SitePlantDistances.Where(d => d.SiteId == request.SiteId && d.PlantId == request.PlantId)
                .Select(d => (decimal?)d.DistanceKm).FirstOrDefaultAsync(cancellationToken);
        if (distance is null)
        {
            return (null, Bad("Enter the distance in km. No distance is stored for this site and plant."));
        }

        if (distance != Math.Round(distance.Value, 2))
        {
            return (null, Bad("The distance can have at most 2 decimal places."));
        }

        var lineIds = request.Lines.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToList();
        var existingLineIds = existing?.Lines.Select(l => l.Id).ToHashSet() ?? [];
        if (lineIds.Distinct().Count() != lineIds.Count || lineIds.Any(i => !existingLineIds.Contains(i)))
        {
            return (null, Bad("Each line id must belong to this order and appear only once."));
        }

        var productIds = request.Lines.Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var existingProductIds = existing?.Lines.Select(l => l.ProductId).ToHashSet() ?? [];

        foreach (var productId in productIds)
        {
            if (!products.TryGetValue(productId, out var product)
                || (product.Status != ActiveStatus.Active && !existingProductIds.Contains(productId)))
            {
                return (null, Bad("Every product must exist in this company and be active."));
            }
        }

        var evaluated = new List<LineEval>();
        foreach (var line in request.Lines)
        {
            if (line.QuantityCft != Math.Round(line.QuantityCft, 2)
                || (line.UnitPrice is { } price && price != Math.Round(price, 2)))
            {
                return (null, Bad("Quantities and prices can have at most 2 decimal places."));
            }

            var product = products[line.ProductId!.Value];
            var match = await finder.FindAsync(product.Id, request.PlantId!.Value, request.CustomerId, request.RequestedDate!.Value, cancellationToken);
            var floor = await calculator.CalculateAsync(
                product.Id, request.PlantId!.Value, request.RequestedDate!.Value, distance.Value, customer.Type, cancellationToken);

            var unitPrice = line.UnitPrice ?? match?.PricePerCft;
            if (unitPrice is null)
            {
                return (null, Bad($"{product.Code}: there is no active price, so enter a unit price."));
            }

            evaluated.Add(new LineEval(
                unitPrice.Value, match?.PriceId, floor.Breakdown?.FloorPrice, floor.Breakdown?.MixDesignId,
                line.SlumpMm ?? product.DefaultSlumpMm));
        }

        return (new Prepared(request.SalesRepId ?? customer.SalesRepId, distance.Value, evaluated), null);
    }

    // What would change, and whether it is a commercial change (price, quantity, lines, distance, date, plant, customer).
    // Call this before Apply.
    private static Changes Compare(Order order, OrderRequest request, Prepared prepared)
    {
        var existing = order.Lines.ToDictionary(l => l.Id);
        var keptIds = request.Lines.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToHashSet();
        var lines = new List<object>();

        var commercial = order.CustomerId != request.CustomerId || order.PlantId != request.PlantId
                         || order.RequestedDate != request.RequestedDate || order.DistanceKm != prepared.DistanceKm;

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var r = request.Lines[i];
            var e = prepared.Lines[i];

            if (r.Id is { } lineId)
            {
                var old = existing[lineId];
                if (old.ProductId != r.ProductId || old.QuantityCft != r.QuantityCft || old.UnitPrice != e.UnitPrice)
                {
                    commercial = true;
                    lines.Add(new
                    {
                        Line = lineId,
                        Before = new { old.ProductId, old.QuantityCft, old.UnitPrice },
                        After = new { r.ProductId, r.QuantityCft, e.UnitPrice }
                    });
                }
            }
            else
            {
                commercial = true;
                lines.Add(new { Added = new { r.ProductId, r.QuantityCft, e.UnitPrice } });
            }
        }

        foreach (var removed in order.Lines.Where(l => !keptIds.Contains(l.Id)))
        {
            commercial = true;
            lines.Add(new { Removed = new { removed.ProductId, removed.QuantityCft, removed.UnitPrice } });
        }

        return new Changes(commercial, new
        {
            Before = new { order.DistanceKm, order.RequestedDate, order.PlantId },
            After = new { prepared.DistanceKm, request.RequestedDate, request.PlantId },
            Lines = lines
        });
    }

    // Updates the header and lines in place, adds new lines through the DbSet, and returns the final set of lines.
    private List<OrderLine> Apply(Order order, OrderRequest request, Prepared prepared)
    {
        order.CustomerId = request.CustomerId!.Value;
        order.SiteId = request.SiteId!.Value;
        order.SalesRepId = prepared.SalesRepId;
        order.PlantId = request.PlantId!.Value;
        order.RequestedDate = request.RequestedDate!.Value;
        order.DistanceKm = prepared.DistanceKm;
        order.PaymentTerms = request.PaymentTerms?.Trim();
        order.Notes = request.Notes?.Trim();

        var keptIds = request.Lines.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToHashSet();
        var removed = order.Lines.Where(l => !keptIds.Contains(l.Id)).ToList();
        var existing = order.Lines.ToDictionary(l => l.Id);
        var final = new List<OrderLine>();

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var r = request.Lines[i];
            var e = prepared.Lines[i];

            if (r.Id is { } lineId)
            {
                var line = existing[lineId];
                line.ProductId = r.ProductId!.Value;
                line.MixDesignId = e.MixDesignId;
                line.QuantityCft = r.QuantityCft;
                line.UnitPrice = e.UnitPrice;
                line.PriceId = e.PriceId;
                line.FloorPrice = e.FloorPrice;
                line.SlumpMm = e.SlumpMm;
                line.PourElement = r.PourElement!.Value;
                line.PourMethod = r.PourMethod!.Value;
                line.Notes = r.Notes?.Trim();
                final.Add(line);
            }
            else
            {
                var line = NewLine(order.TenantId, r, e);
                line.OrderId = order.Id;
                db.OrderLines.Add(line);
                final.Add(line);
            }
        }

        db.OrderLines.RemoveRange(removed);
        return final;
    }

    private static OrderLine NewLine(Guid tenantId, OrderLineRequest r, LineEval e) => new()
    {
        TenantId = tenantId,
        ProductId = r.ProductId!.Value,
        MixDesignId = e.MixDesignId,
        QuantityCft = r.QuantityCft,
        UnitPrice = e.UnitPrice,
        PriceId = e.PriceId,
        FloorPrice = e.FloorPrice,
        SlumpMm = e.SlumpMm,
        PourElement = r.PourElement!.Value,
        PourMethod = r.PourMethod!.Value,
        Notes = r.Notes?.Trim()
    };

    // Only for a brand-new order, which is added to the context as a whole.
    private OrderStatusHistory HistoryEntry(Order order, OrderStatus? from, OrderStatus to, string? reason) => new()
    {
        TenantId = order.TenantId,
        OrderId = order.Id,
        FromStatus = from,
        ToStatus = to,
        ChangedBy = currentUser.UserId,
        Reason = reason
    };

    private async Task<OrderDetailDto?> LoadDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var summary = await db.Orders.AsNoTracking().Where(o => o.Id == id).Select(ToSummary)
            .FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        var lines = await db.OrderLines.AsNoTracking().Where(l => l.OrderId == id)
            .OrderBy(l => l.Product.Code).ThenBy(l => l.PourElement).Select(ToLine).ToListAsync(cancellationToken);

        var history = await db.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == id)
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => new OrderHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAtUtc, h.ChangedBy, h.Reason))
            .ToListAsync(cancellationToken);

        var approvals = await db.OrderApprovals.AsNoTracking().Where(a => a.OrderId == id)
            .OrderBy(a => a.Round).ThenBy(a => a.OpenedAtUtc)
            .Select(a => new OrderApprovalDto(a.Stage, a.Decision, a.Round, a.OpenedAtUtc, a.DecidedBy, a.DecidedAtUtc, a.Comment))
            .ToListAsync(cancellationToken);

        var charges = await db.OrderCharges.AsNoTracking().Where(c => c.OrderId == id).OrderBy(c => c.Description)
            .Select(c => new OrderChargeDto(c.Kind, c.Description, c.QuantityCft, c.RatePerCft, c.Amount))
            .ToListAsync(cancellationToken);

        return new OrderDetailDto(summary, lines, history, approvals, charges);
    }
}