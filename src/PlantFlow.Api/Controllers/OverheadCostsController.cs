using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record OverheadCostDto(
    Guid Id, OverheadKind Kind, string Name, Guid? PlantId, string? PlantCode,
    decimal AmountPerCft, DateOnly ValidFrom, DateOnly? ValidTo,
    CostStatus Status, string? RejectionReason, DateTime? ApprovedAtUtc);

public class OverheadCostRequest
{
    [Required]
    public OverheadKind? Kind { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Empty means every plant. A bridge toll must name its plant.
    public Guid? PlantId { get; init; }

    // Per CFT. At most 4 decimals.
    [Range(0.0001, 100000.0)]
    public decimal AmountPerCft { get; init; }

    [Required]
    public DateOnly? ValidFrom { get; init; }

    // Empty means it has no end date.
    public DateOnly? ValidTo { get; init; }
}

[ApiController]
[Route("api/overhead-costs")]
public class OverheadCostsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<OverheadCost, OverheadCostDto>> ToDto = c => new OverheadCostDto(
        c.Id, c.Kind, c.Name, c.PlantId, c.Plant != null ? c.Plant.Code : null, c.AmountPerCft, c.ValidFrom, c.ValidTo,
        c.Status, c.RejectionReason, c.ApprovedAtUtc);

    // ?search= matches the name.  ?kind=  ?plantId=  ?status=
    [HttpGet]
    [HasPermission(Permissions.CostsManage, Permissions.CostsApprove)]
    public async Task<ActionResult<PagedResult<OverheadCostDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] OverheadKind? kind, [FromQuery] Guid? plantId, [FromQuery] CostStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.OverheadCosts.AsNoTracking();

        if (kind is not null) query = query.Where(c => c.Kind == kind);
        if (plantId is not null) query = query.Where(c => c.PlantId == plantId);
        if (status is not null) query = query.Where(c => c.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(c => c.Name.Contains(search));
        }

        query = query.OrderBy(c => c.Kind).ThenBy(c => c.Name).ThenByDescending(c => c.ValidFrom);
        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CostsManage, Permissions.CostsApprove)]
    public async Task<ActionResult<OverheadCostDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await db.OverheadCosts.AsNoTracking().Where(c => c.Id == id).Select(ToDto)
            .FirstOrDefaultAsync(cancellationToken);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<OverheadCostDto>> Create(OverheadCostRequest request, CancellationToken cancellationToken)
    {
        var problem = await CheckAsync(request, null, cancellationToken);
        if (problem is not null) return problem;

        var cost = new OverheadCost
        {
            TenantId = currentUser.TenantId!.Value,
            Kind = request.Kind!.Value,
            Name = request.Name.Trim(),
            PlantId = request.PlantId,
            AmountPerCft = request.AmountPerCft,
            ValidFrom = request.ValidFrom!.Value,
            ValidTo = request.ValidTo
        };

        db.OverheadCosts.Add(cost);
        audit.Record(cost.TenantId, "overheadcost.created", "OverheadCost", cost.Id.ToString(),
            new { cost.Kind, cost.Name, cost.PlantId, cost.AmountPerCft, cost.ValidFrom, cost.ValidTo });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.OverheadCosts.AsNoTracking().Where(c => c.Id == cost.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = cost.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Update(Guid id, OverheadCostRequest request, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.RequireDraft(cost, "overhead", "edited") is { } notDraft)
        {
            return Problem(title: notDraft, statusCode: StatusCodes.Status409Conflict);
        }

        var problem = await CheckAsync(request, cost.PlantId, cancellationToken);
        if (problem is not null) return problem;

        var before = new { cost.Kind, cost.Name, cost.PlantId, cost.AmountPerCft, cost.ValidFrom, cost.ValidTo };

        cost.Kind = request.Kind!.Value;
        cost.Name = request.Name.Trim();
        cost.PlantId = request.PlantId;
        cost.AmountPerCft = request.AmountPerCft;
        cost.ValidFrom = request.ValidFrom!.Value;
        cost.ValidTo = request.ValidTo;

        audit.Record(cost.TenantId, "overheadcost.updated", "OverheadCost", cost.Id.ToString(),
            new { Before = before, After = new { cost.Kind, cost.Name, cost.PlantId, cost.AmountPerCft, cost.ValidFrom, cost.ValidTo } });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Only a draft can be removed. An approved overhead is retired instead, so the history stays.
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.RequireDraft(cost, "overhead", "deleted") is { } notDraft)
        {
            return Problem(title: notDraft, statusCode: StatusCodes.Status409Conflict);
        }

        db.OverheadCosts.Remove(cost);
        audit.Record(cost.TenantId, "overheadcost.deleted", "OverheadCost", cost.Id.ToString(),
            new { cost.Kind, cost.Name, cost.PlantId, cost.AmountPerCft });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // The plant is only re-checked when it changed.
    private async Task<ObjectResult?> CheckAsync(OverheadCostRequest request, Guid? unchangedPlantId, CancellationToken cancellationToken)
    {
        if (request.AmountPerCft != Math.Round(request.AmountPerCft, 4))
        {
            return Problem(title: "The amount can have at most 4 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidTo < request.ValidFrom)
        {
            return Problem(title: "The end date cannot be before the start date.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Kind == OverheadKind.BridgeToll && request.PlantId is null)
        {
            return Problem(title: "A bridge toll belongs to one plant. Choose the plant.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PlantId is { } plantId && plantId != unchangedPlantId
            && !await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
        {
            return Problem(title: "Choose a plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    // Management approval. From then on the floor price uses it, between its start and end dates.
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.RequirePending(cost, "overhead") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        CostWorkflow.MarkApproved(cost, currentUser.UserId);
        audit.Record(cost.TenantId, "overheadcost.approved", "OverheadCost", cost.Id.ToString(),
            new { cost.Kind, cost.Name, cost.PlantId, cost.AmountPerCft, cost.ValidFrom, cost.ValidTo });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.Submit(cost, "overhead") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(cost.TenantId, "overheadcost.submitted", "OverheadCost", cost.Id.ToString(), new { cost.Status });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Reject(Guid id, RejectCostRequest request, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.Reject(cost, request.Reason, "overhead") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(cost.TenantId, "overheadcost.rejected", "OverheadCost", cost.Id.ToString(), new { cost.RejectionReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Ends an approved row right away. The floor price ignores it from then on.
    [HttpPost("{id:guid}/retire")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken)
    {
        var cost = await db.OverheadCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (CostWorkflow.Retire(cost, "overhead") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(cost.TenantId, "overheadcost.retired", "OverheadCost", cost.Id.ToString(), new { cost.Status });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
