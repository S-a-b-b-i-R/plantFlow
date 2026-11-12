using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Pricing;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record PriceDto(
    Guid Id, Guid ProductId, string ProductCode, Guid? PlantId, string? PlantCode, Guid? CustomerId, string? CustomerCode,
    decimal PricePerCft, DateOnly ValidFrom, DateOnly? ValidTo, PriceStatus Status, string? RejectionReason, DateTime? ApprovedAtUtc);

public class PriceRequest
{
    [Required]
    public Guid? ProductId { get; init; }

    // Empty means every plant.
    public Guid? PlantId { get; init; }

    // Empty means every customer.
    public Guid? CustomerId { get; init; }

    // Per CFT, excluding VAT. At most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal PricePerCft { get; init; }

    [Required]
    public DateOnly? ValidFrom { get; init; }

    // Empty means it has no end date.
    public DateOnly? ValidTo { get; init; }
}

public class RejectPriceRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
[Route("api/prices")]
public class PricesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, PriceFinder finder) : ControllerBase
{
    private static readonly Expression<Func<Price, PriceDto>> ToDto = p => new PriceDto(
        p.Id, p.ProductId, p.Product.Code, p.PlantId, p.Plant != null ? p.Plant.Code : null,
        p.CustomerId, p.Customer != null ? p.Customer.Code : null,
        p.PricePerCft, p.ValidFrom, p.ValidTo, p.Status, p.RejectionReason, p.ApprovedAtUtc);

    // ?search= matches product code or name, or customer name.  ?productId= ?plantId= ?customerId= ?status=
    [HttpGet]
    [HasPermission(Permissions.PricesView)]
    public async Task<ActionResult<PagedResult<PriceDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? productId, [FromQuery] Guid? plantId,
        [FromQuery] Guid? customerId, [FromQuery] PriceStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Prices.AsNoTracking();

        if (productId is not null) query = query.Where(p => p.ProductId == productId);
        if (plantId is not null) query = query.Where(p => p.PlantId == plantId);
        if (customerId is not null) query = query.Where(p => p.CustomerId == customerId);
        if (status is not null) query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(p => p.Product.Code.Contains(search) || p.Product.Name.Contains(search)
                                     || (p.Customer != null && p.Customer.Name.Contains(search)));
        }

        query = query.OrderBy(p => p.Product.Code).ThenBy(p => p.Plant != null ? p.Plant.Code : "")
            .ThenBy(p => p.Customer != null ? p.Customer.Code : "").ThenByDescending(p => p.ValidFrom);

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PricesView)]
    public async Task<ActionResult<PriceDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await db.Prices.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        return dto is null ? NotFound() : dto;
    }

    // The price that applies for a product at a plant, optionally for a customer, on a date (default today).
    // The most specific one wins: customer+plant, customer, plant, then general.
    [HttpGet("lookup")]
    [HasPermission(Permissions.PricesView)]
    public async Task<ActionResult<PriceMatch>> Lookup(
        [FromQuery] Guid productId, [FromQuery] Guid plantId, [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return Problem(title: "Choose a product of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
        {
            return Problem(title: "Choose a plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (customerId is { } cid && !await db.Customers.AnyAsync(c => c.Id == cid, cancellationToken))
        {
            return Problem(title: "Choose a customer of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        var match = await finder.FindAsync(
            productId, plantId, customerId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        return match is null
            ? Problem(title: "There is no active price for this product, plant and customer on that date.",
                statusCode: StatusCodes.Status404NotFound)
            : match;
    }

    [HttpPost]
    [HasPermission(Permissions.PricesEdit)]
    public async Task<ActionResult<PriceDto>> Create(PriceRequest request, CancellationToken cancellationToken)
    {
        var problem = await CheckAsync(request, null, null, null, cancellationToken);
        if (problem is not null) return problem;

        var price = new Price
        {
            TenantId = currentUser.TenantId!.Value,
            ProductId = request.ProductId!.Value,
            PlantId = request.PlantId,
            CustomerId = request.CustomerId,
            PricePerCft = request.PricePerCft,
            ValidFrom = request.ValidFrom!.Value,
            ValidTo = request.ValidTo
        };

        db.Prices.Add(price);
        audit.Record(price.TenantId, "price.created", "Price", price.Id.ToString(),
            new { price.ProductId, price.PlantId, price.CustomerId, price.PricePerCft, price.ValidFrom, price.ValidTo });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Prices.AsNoTracking().Where(p => p.Id == price.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = price.Id }, dto);
    }

    // Only a draft can be edited. Products, plants and customers are only re-checked when they changed.
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PricesEdit)]
    public async Task<IActionResult> Update(Guid id, PriceRequest request, CancellationToken cancellationToken)
    {
        var price = await db.Prices.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (price is null) return NotFound();

        if (price.Status != PriceStatus.Draft)
        {
            return Problem(title: $"Only a draft can be edited. This price is {price.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var problem = await CheckAsync(request, price.ProductId, price.PlantId, price.CustomerId, cancellationToken);
        if (problem is not null) return problem;

        price.ProductId = request.ProductId!.Value;
        price.PlantId = request.PlantId;
        price.CustomerId = request.CustomerId;
        price.PricePerCft = request.PricePerCft;
        price.ValidFrom = request.ValidFrom!.Value;
        price.ValidTo = request.ValidTo;

        audit.Record(price.TenantId, "price.updated", "Price", price.Id.ToString(),
            new { price.ProductId, price.PlantId, price.CustomerId, price.PricePerCft, price.ValidFrom, price.ValidTo });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.PricesEdit)]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, PriceStatus.Draft, PriceStatus.PendingApproval, "price.submitted",
            p => p.RejectionReason = null, null, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.PricesApprove)]
    public Task<IActionResult> Reject(Guid id, RejectPriceRequest request, CancellationToken cancellationToken) =>
        MoveAsync(id, PriceStatus.PendingApproval, PriceStatus.Draft, "price.rejected",
            p => p.RejectionReason = request.Reason.Trim(), null, cancellationToken);

    // Ends an active price right away. The lookup ignores it from then on.
    [HttpPost("{id:guid}/expire")]
    [HasPermission(Permissions.PricesApprove)]
    public Task<IActionResult> Expire(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, PriceStatus.Active, PriceStatus.Expired, "price.expired", _ => { }, null, cancellationToken);

    // Management approval. Two active prices of the same product, plant and customer scope may not overlap in time.
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.PricesApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        // Serializable, so two approvals at the same moment cannot both pass the overlap check.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var price = await db.Prices.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (price is null) return NotFound();

        if (price.Status != PriceStatus.PendingApproval)
        {
            return Problem(title: $"Only a price waiting for approval can be approved. This price is {price.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var sameScope = await db.Prices.AsNoTracking()
            .Where(p => p.Id != price.Id && p.Status == PriceStatus.Active && p.ProductId == price.ProductId
                        && p.PlantId == price.PlantId && p.CustomerId == price.CustomerId)
            .ToListAsync(cancellationToken);

        var clash = sameScope.FirstOrDefault(p =>
            price.ValidFrom <= (p.ValidTo ?? DateOnly.MaxValue) && p.ValidFrom <= (price.ValidTo ?? DateOnly.MaxValue));

        if (clash is not null)
        {
            var until = clash.ValidTo is { } to ? to.ToString("yyyy-MM-dd") : "no end date";
            return Problem(
                title: $"An active price of {clash.PricePerCft:0.##} already covers part of this period "
                       + $"({clash.ValidFrom:yyyy-MM-dd} to {until}). Expire it or end it before this one starts.",
                statusCode: StatusCodes.Status409Conflict);
        }

        price.Status = PriceStatus.Active;
        price.ApprovedBy = currentUser.UserId;
        price.ApprovedAtUtc = DateTime.UtcNow;

        audit.Record(price.TenantId, "price.approved", "Price", price.Id.ToString(),
            new { price.ProductId, price.PlantId, price.CustomerId, price.PricePerCft, price.ValidFrom, price.ValidTo });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> MoveAsync(
        Guid id, PriceStatus from, PriceStatus to, string action, Action<Price> apply, object? _, CancellationToken cancellationToken)
    {
        var price = await db.Prices.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (price is null) return NotFound();

        if (price.Status != from)
        {
            return Problem(title: $"Only a {from} price can become {to}. This price is {price.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        price.Status = to;
        apply(price);

        audit.Record(price.TenantId, action, "Price", price.Id.ToString(),
            new { From = from.ToString(), To = to.ToString(), price.RejectionReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckAsync(
        PriceRequest request, Guid? unchangedProductId, Guid? unchangedPlantId, Guid? unchangedCustomerId,
        CancellationToken cancellationToken)
    {
        if (request.PricePerCft != Math.Round(request.PricePerCft, 2))
        {
            return Problem(title: "The price can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidTo < request.ValidFrom)
        {
            return Problem(title: "The end date cannot be before the start date.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ProductId != unchangedProductId
            && !await db.Products.AnyAsync(p => p.Id == request.ProductId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active product of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PlantId is { } plantId && plantId != unchangedPlantId
            && !await db.Plants.AnyAsync(p => p.Id == plantId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CustomerId is { } customerId && customerId != unchangedCustomerId
            && !await db.Customers.AnyAsync(c => c.Id == customerId && c.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active customer of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }
}