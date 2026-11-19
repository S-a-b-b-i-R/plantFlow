using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record DeliveryChargeBandDto(
    Guid Id, Guid? PlantId, string? PlantCode, decimal FromKm, decimal? ToKm, decimal ChargePerCft,
    CostStatus Status, string? RejectionReason, DateTime? ApprovedAtUtc);

public class DeliveryChargeBandRequest
{
    // Empty means every plant that has no bands of its own.
    public Guid? PlantId { get; init; }

    // The band applies when the distance is MORE than this. 15 means free up to and including 15 km.
    [Range(0.0, 10000.0)]
    public decimal FromKm { get; init; }

    // Up to and including this. Empty means no upper limit.
    [Range(0.01, 10000.0)]
    public decimal? ToKm { get; init; }

    // Per CFT. At most 4 decimals.
    [Range(0.0001, 100000.0)]
    public decimal ChargePerCft { get; init; }
}

[ApiController]
[Route("api/delivery-charges")]
public class DeliveryChargesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<DeliveryChargeBand, DeliveryChargeBandDto>> ToDto = b => new DeliveryChargeBandDto(
        b.Id, b.PlantId, b.Plant != null ? b.Plant.Code : null, b.FromKm, b.ToKm, b.ChargePerCft,
        b.Status, b.RejectionReason, b.ApprovedAtUtc);

    // ?plantId=  ?status=
    [HttpGet]
    [HasPermission(Permissions.CostsManage, Permissions.CostsApprove)]
    public async Task<ActionResult<PagedResult<DeliveryChargeBandDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? plantId, [FromQuery] CostStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.DeliveryChargeBands.AsNoTracking();
        if (plantId is not null) query = query.Where(b => b.PlantId == plantId);
        if (status is not null) query = query.Where(b => b.Status == status);

        query = query.OrderBy(b => b.Plant != null ? b.Plant.Code : "").ThenBy(b => b.FromKm);
        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CostsManage, Permissions.CostsApprove)]
    public async Task<ActionResult<DeliveryChargeBandDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await db.DeliveryChargeBands.AsNoTracking().Where(b => b.Id == id).Select(ToDto)
            .FirstOrDefaultAsync(cancellationToken);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<DeliveryChargeBandDto>> Create(DeliveryChargeBandRequest request, CancellationToken cancellationToken)
    {
        var problem = await CheckAsync(request, null, cancellationToken);
        if (problem is not null) return problem;

        var band = new DeliveryChargeBand
        {
            TenantId = currentUser.TenantId!.Value,
            PlantId = request.PlantId,
            FromKm = request.FromKm,
            ToKm = request.ToKm,
            ChargePerCft = request.ChargePerCft
        };

        db.DeliveryChargeBands.Add(band);
        audit.Record(band.TenantId, "deliverycharge.created", "DeliveryChargeBand", band.Id.ToString(),
            new { band.PlantId, band.FromKm, band.ToKm, band.ChargePerCft });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.DeliveryChargeBands.AsNoTracking().Where(b => b.Id == band.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = band.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Update(Guid id, DeliveryChargeBandRequest request, CancellationToken cancellationToken)
    {
        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.RequireDraft(band, "delivery band", "edited") is { } notDraft)
        {
            return Problem(title: notDraft, statusCode: StatusCodes.Status409Conflict);
        }

        var problem = await CheckAsync(request, band.PlantId, cancellationToken);
        if (problem is not null) return problem;

        band.PlantId = request.PlantId;
        band.FromKm = request.FromKm;
        band.ToKm = request.ToKm;
        band.ChargePerCft = request.ChargePerCft;

        audit.Record(band.TenantId, "deliverycharge.updated", "DeliveryChargeBand", band.Id.ToString(),
            new { band.PlantId, band.FromKm, band.ToKm, band.ChargePerCft });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Only a draft can be removed. An approved band is retired instead.
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.RequireDraft(band, "delivery band", "deleted") is { } notDraft)
        {
            return Problem(title: notDraft, statusCode: StatusCodes.Status409Conflict);
        }

        db.DeliveryChargeBands.Remove(band);
        audit.Record(band.TenantId, "deliverycharge.deleted", "DeliveryChargeBand", band.Id.ToString(),
            new { band.PlantId, band.FromKm, band.ToKm, band.ChargePerCft });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckAsync(
        DeliveryChargeBandRequest request, Guid? unchangedPlantId, CancellationToken cancellationToken)
    {
        if (request.ChargePerCft != Math.Round(request.ChargePerCft, 4)
            || request.FromKm != Math.Round(request.FromKm, 2)
            || (request.ToKm is { } toKm && toKm != Math.Round(toKm, 2)))
        {
            return Problem(title: "Distances can have 2 decimals and the charge 4.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ToKm is { } upper && upper <= request.FromKm)
        {
            return Problem(title: "The upper distance must be more than the lower distance.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PlantId is { } plantId && plantId != unchangedPlantId
            && !await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
        {
            return Problem(title: "Choose a plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    // Management approval. Approved bands of the same plant (or the general ones) must not overlap,
    // so retire the old band first when this one replaces it.
    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        // Serializable, so two approvals at the same moment cannot both pass the overlap check.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.RequirePending(band, "delivery band") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        var siblings = await db.DeliveryChargeBands.AsNoTracking()
            .Where(b => b.Id != band.Id && b.PlantId == band.PlantId && b.Status == CostStatus.Approved)
            .ToListAsync(cancellationToken);

        var to = band.ToKm ?? decimal.MaxValue;
        var clash = siblings.FirstOrDefault(b => band.FromKm < (b.ToKm ?? decimal.MaxValue) && b.FromKm < to);
        if (clash is not null)
        {
            var upper = clash.ToKm is { } km ? $"{km:0.##} km" : "no limit";
            return Problem(
                title: $"An approved band from {clash.FromKm:0.##} km to {upper} already covers part of this range. Retire it first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        CostWorkflow.MarkApproved(band, currentUser.UserId);
        audit.Record(band.TenantId, "deliverycharge.approved", "DeliveryChargeBand", band.Id.ToString(),
            new { band.PlantId, band.FromKm, band.ToKm, band.ChargePerCft });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.Submit(band, "delivery band") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(band.TenantId, "deliverycharge.submitted", "DeliveryChargeBand", band.Id.ToString(), new { band.Status });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Reject(Guid id, RejectCostRequest request, CancellationToken cancellationToken)
    {
        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.Reject(band, request.Reason, "delivery band") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(band.TenantId, "deliverycharge.rejected", "DeliveryChargeBand", band.Id.ToString(), new { band.RejectionReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Ends an approved row right away. The floor price ignores it from then on.
    [HttpPost("{id:guid}/retire")]
    [HasPermission(Permissions.CostsApprove)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken)
    {
        var band = await db.DeliveryChargeBands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (band is null) return NotFound();

        if (CostWorkflow.Retire(band, "delivery band") is { } why)
        {
            return Problem(title: why, statusCode: StatusCodes.Status409Conflict);
        }

        audit.Record(band.TenantId, "deliverycharge.retired", "DeliveryChargeBand", band.Id.ToString(), new { band.Status });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
