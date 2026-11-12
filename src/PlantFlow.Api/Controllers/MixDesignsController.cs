using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record MixDesignSummaryDto(
    Guid Id, Guid ProductId, string ProductCode, Guid? PlantId, string? PlantCode, int Version,
    MixDesignKind Kind, MixDesignStatus Status, int ItemCount, string? Notes, string? RejectionReason,
    DateTime? ApprovedAtUtc);

public record MixDesignItemDto(Guid MaterialId, string MaterialCode, string MaterialName, MaterialUnit Unit, decimal QuantityPerCft);

public record MixDesignDetailDto(MixDesignSummaryDto Design, List<MixDesignItemDto> Items);

public class MixDesignItemRequest
{
    [Required]
    public Guid MaterialId { get; init; }

    // Per CFT of concrete, in the material's own unit. At most 4 decimals.
    [Range(0.0001, 100000.0)]
    public decimal QuantityPerCft { get; init; }
}

public class CreateMixDesignRequest
{
    [Required]
    public Guid ProductId { get; init; }

    // Empty means the general recipe.
    public Guid? PlantId { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [Required, MinLength(1)]
    public List<MixDesignItemRequest> Items { get; init; } = [];
}

public class UpdateMixDesignRequest
{
    [StringLength(1000)]
    public string? Notes { get; init; }

    // Replaces the whole list.
    [Required, MinLength(1)]
    public List<MixDesignItemRequest> Items { get; init; } = [];
}

public class RejectMixDesignRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
[Route("api/mix-designs")]
public class MixDesignsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<MixDesign, MixDesignSummaryDto>> ToSummary = d => new MixDesignSummaryDto(
        d.Id, d.ProductId, d.Product.Code, d.PlantId, d.Plant != null ? d.Plant.Code : null, d.Version,
        d.Kind, d.Status, d.Items.Count(), d.Notes, d.RejectionReason, d.ApprovedAtUtc);

    // ?search= matches product code or name.  ?productId=  ?plantId=  ?status=  ?kind=
    // ?sort=product|-product|status. The default is by product, then newest version first.
    [HttpGet]
    [HasPermission(Permissions.MixDesignsView)]
    public async Task<ActionResult<PagedResult<MixDesignSummaryDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? productId, [FromQuery] Guid? plantId,
        [FromQuery] MixDesignStatus? status, [FromQuery] MixDesignKind? kind, CancellationToken cancellationToken)
    {
        var query = db.MixDesigns.AsNoTracking();

        if (productId is not null) query = query.Where(d => d.ProductId == productId);
        if (plantId is not null) query = query.Where(d => d.PlantId == plantId);
        if (status is not null) query = query.Where(d => d.Status == status);
        if (kind is not null) query = query.Where(d => d.Kind == kind);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(d => d.Product.Code.Contains(search) || d.Product.Name.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "-product" => query.OrderByDescending(d => d.Product.Code).ThenByDescending(d => d.Version),
            "status" => query.OrderBy(d => d.Status).ThenBy(d => d.Product.Code).ThenByDescending(d => d.Version),
            _ => query.OrderBy(d => d.Product.Code).ThenBy(d => d.Plant != null ? d.Plant.Code : "")
                     .ThenByDescending(d => d.Version)
        };

        return await query.Select(ToSummary).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MixDesignsView)]
    public async Task<ActionResult<MixDesignDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(id, cancellationToken);
        if (detail is null) return NotFound();
        return detail;
    }

    // Starts a new draft version for a product (and optionally a plant).
    [HttpPost]
    [HasPermission(Permissions.MixDesignsEdit)]
    public async Task<ActionResult<MixDesignDetailDto>> Create(CreateMixDesignRequest request, CancellationToken cancellationToken)
    {
        // The tenant filter means a product or plant of another company is simply not found.
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active product of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PlantId is { } plantId
            && !await db.Plants.AnyAsync(p => p.Id == plantId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        var itemsProblem = await CheckItemsAsync(request.Items, cancellationToken);
        if (itemsProblem is not null) return itemsProblem;

        var inScope = db.MixDesigns.Where(d =>
            d.ProductId == request.ProductId && d.PlantId == request.PlantId && d.Kind == MixDesignKind.Standard);

        if (await inScope.AnyAsync(
                d => d.Status == MixDesignStatus.Draft || d.Status == MixDesignStatus.PendingApproval, cancellationToken))
        {
            return Problem(
                title: "There is already a draft or a design waiting for approval for this product and plant. Finish or reject it first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = currentUser.TenantId!.Value;
        var design = new MixDesign
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            PlantId = request.PlantId,
            Version = (await inScope.MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0) + 1,
            Notes = request.Notes?.Trim(),
            Items = request.Items.Select(i => new MixDesignItem
            {
                TenantId = tenantId,
                MaterialId = i.MaterialId,
                QuantityPerCft = i.QuantityPerCft
            }).ToList()
        };

        db.MixDesigns.Add(design);
        audit.Record(tenantId, "mixdesign.created", "MixDesign", design.Id.ToString(),
            new { design.ProductId, design.PlantId, design.Version });
        await db.SaveChangesAsync(cancellationToken);

        var detail = await LoadDetailAsync(design.Id, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = design.Id }, detail);
    }

    // Only a draft can be edited. An approved recipe is never changed: create a new version instead.
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MixDesignsEdit)]
    public async Task<IActionResult> Update(Guid id, UpdateMixDesignRequest request, CancellationToken cancellationToken)
    {
        var design = await db.MixDesigns.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (design is null) return NotFound();

        if (design.Status != MixDesignStatus.Draft)
        {
            return Problem(title: $"Only a draft can be edited. This design is {design.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var itemsProblem = await CheckItemsAsync(request.Items, cancellationToken);
        if (itemsProblem is not null) return itemsProblem;

        // Update in place so the same material is never deleted and re-inserted in one save.
        var existing = design.Items.ToDictionary(i => i.MaterialId);
        foreach (var wanted in request.Items)
        {
            if (existing.TryGetValue(wanted.MaterialId, out var current))
            {
                current.QuantityPerCft = wanted.QuantityPerCft;
            }
            else
            {
                db.MixDesignItems.Add(new MixDesignItem
                {
                    TenantId = design.TenantId,
                    MixDesignId = design.Id,
                    MaterialId = wanted.MaterialId,
                    QuantityPerCft = wanted.QuantityPerCft
                });
            }
        }

        var wantedIds = request.Items.Select(i => i.MaterialId).ToHashSet();
        db.MixDesignItems.RemoveRange(design.Items.Where(i => !wantedIds.Contains(i.MaterialId)));

        design.Notes = request.Notes?.Trim();

        audit.Record(design.TenantId, "mixdesign.updated", "MixDesign", design.Id.ToString(),
            new { design.Version, Items = request.Items.Select(i => new { i.MaterialId, i.QuantityPerCft }) });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.MixDesignsEdit)]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, MixDesignStatus.Draft, MixDesignStatus.PendingApproval, "mixdesign.submitted",
            d => d.RejectionReason = null, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.MixDesignsApprove)]
    public Task<IActionResult> Reject(Guid id, RejectMixDesignRequest request, CancellationToken cancellationToken) =>
        MoveAsync(id, MixDesignStatus.PendingApproval, MixDesignStatus.Draft, "mixdesign.rejected",
            d => d.RejectionReason = request.Reason.Trim(), cancellationToken);

    [HttpPost("{id:guid}/retire")]
    [HasPermission(Permissions.MixDesignsApprove)]
    public Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, MixDesignStatus.Approved, MixDesignStatus.Retired, "mixdesign.retired",
            d => d.RetiredAtUtc = DateTime.UtcNow, cancellationToken);

    // Approving a version retires the previously approved one of the same product and plant.
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.MixDesignsApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var design = await db.MixDesigns.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (design is null) return NotFound();

        if (design.Status != MixDesignStatus.PendingApproval)
        {
            return Problem(title: $"Only a design waiting for approval can be approved. This design is {design.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var previous = await db.MixDesigns.FirstOrDefaultAsync(d =>
            d.ProductId == design.ProductId && d.PlantId == design.PlantId
            && d.Kind == MixDesignKind.Standard && d.Status == MixDesignStatus.Approved, cancellationToken);

        // Two saves in one transaction: the old version must be retired before the new one is approved,
        // because the database allows only one approved version per product and plant.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (previous is not null)
        {
            previous.Status = MixDesignStatus.Retired;
            previous.RetiredAtUtc = DateTime.UtcNow;
            audit.Record(previous.TenantId, "mixdesign.retired", "MixDesign", previous.Id.ToString(),
                new { previous.Version, Reason = $"Replaced by version {design.Version}" });
            await db.SaveChangesAsync(cancellationToken);
        }

        design.Status = MixDesignStatus.Approved;
        design.ApprovedBy = currentUser.UserId;
        design.ApprovedAtUtc = DateTime.UtcNow;
        audit.Record(design.TenantId, "mixdesign.approved", "MixDesign", design.Id.ToString(),
            new { design.Version, RetiredPreviousVersion = previous?.Version });
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> MoveAsync(
        Guid id, MixDesignStatus from, MixDesignStatus to, string action, Action<MixDesign> apply,
        CancellationToken cancellationToken)
    {
        var design = await db.MixDesigns.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (design is null) return NotFound();

        if (design.Status != from)
        {
            return Problem(title: $"Only a {from} design can become {to}. This design is {design.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (to == MixDesignStatus.PendingApproval && design.Items.Count == 0)
        {
            return Problem(title: "Add at least one material before submitting.", statusCode: StatusCodes.Status409Conflict);
        }

        design.Status = to;
        apply(design);

        audit.Record(design.TenantId, action, "MixDesign", design.Id.ToString(),
            new { design.Version, From = from.ToString(), To = to.ToString(), design.RejectionReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckItemsAsync(List<MixDesignItemRequest> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.MaterialId).ToList();

        if (ids.Distinct().Count() != ids.Count)
        {
            return Problem(title: "Each material can appear only once in a mix design.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (items.Any(i => i.QuantityPerCft != Math.Round(i.QuantityPerCft, 4)))
        {
            return Problem(title: "Quantities can have at most 4 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        var activeCount = await db.Materials.CountAsync(m => ids.Contains(m.Id) && m.Status == ActiveStatus.Active, cancellationToken);
        if (activeCount != ids.Count)
        {
            return Problem(title: "Every material must exist in this company and be active.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private async Task<MixDesignDetailDto?> LoadDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var summary = await db.MixDesigns.AsNoTracking()
            .Where(d => d.Id == id).Select(ToSummary).FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        var items = await db.MixDesignItems.AsNoTracking()
            .Where(i => i.MixDesignId == id)
            .OrderBy(i => i.Material.Code)
            .Select(i => new MixDesignItemDto(i.MaterialId, i.Material.Code, i.Material.Name, i.Material.Unit, i.QuantityPerCft))
            .ToListAsync(cancellationToken);

        return new MixDesignDetailDto(summary, items);
    }
}