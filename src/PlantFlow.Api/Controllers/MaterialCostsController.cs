using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record MaterialCostDto(
    Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, MaterialUnit Unit,
    Guid? PlantId, string? PlantCode, decimal CostPerUnit, DateOnly EffectiveFrom);

public class CreateMaterialCostRequest
{
    [Required]
    public Guid? MaterialId { get; init; }

    // Empty means every plant.
    public Guid? PlantId { get; init; }

    // Per kg, litre or tonne, whatever the material's unit is. At most 4 decimals.
    [Range(0.0001, 1000000.0)]
    public decimal CostPerUnit { get; init; }

    [Required]
    public DateOnly? EffectiveFrom { get; init; }
}

public class UpdateMaterialCostRequest
{
    [Range(0.0001, 1000000.0)]
    public decimal CostPerUnit { get; init; }

    [Required]
    public DateOnly? EffectiveFrom { get; init; }
}

[ApiController]
[Route("api/material-costs")]
public class MaterialCostsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<MaterialCost, MaterialCostDto>> ToDto = c => new MaterialCostDto(
        c.Id, c.MaterialId, c.Material.Code, c.Material.Name, c.Material.Unit,
        c.PlantId, c.Plant != null ? c.Plant.Code : null, c.CostPerUnit, c.EffectiveFrom);

    // ?search= matches material code or name.  ?materialId=  ?plantId=
    [HttpGet]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<PagedResult<MaterialCostDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? materialId, [FromQuery] Guid? plantId, CancellationToken cancellationToken)
    {
        var query = db.MaterialCosts.AsNoTracking();

        if (materialId is not null) query = query.Where(c => c.MaterialId == materialId);
        if (plantId is not null) query = query.Where(c => c.PlantId == plantId);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(c => c.Material.Code.Contains(search) || c.Material.Name.Contains(search));
        }

        query = query.OrderBy(c => c.Material.Code).ThenBy(c => c.Plant != null ? c.Plant.Code : "")
            .ThenByDescending(c => c.EffectiveFrom);

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<MaterialCostDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await db.MaterialCosts.AsNoTracking().Where(c => c.Id == id).Select(ToDto)
            .FirstOrDefaultAsync(cancellationToken);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<MaterialCostDto>> Create(CreateMaterialCostRequest request, CancellationToken cancellationToken)
    {
        if (request.CostPerUnit != Math.Round(request.CostPerUnit, 4))
        {
            return Problem(title: "The cost can have at most 4 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Materials.AnyAsync(m => m.Id == request.MaterialId, cancellationToken))
        {
            return Problem(title: "Choose a material of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PlantId is { } plantId && !await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
        {
            return Problem(title: "Choose a plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.MaterialCosts.AnyAsync(c =>
                c.MaterialId == request.MaterialId && c.PlantId == request.PlantId && c.EffectiveFrom == request.EffectiveFrom,
                cancellationToken))
        {
            return Problem(title: "This material already has a cost from that date for that plant. Edit it instead.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var cost = new MaterialCost
        {
            TenantId = currentUser.TenantId!.Value,
            MaterialId = request.MaterialId!.Value,
            PlantId = request.PlantId,
            CostPerUnit = request.CostPerUnit,
            EffectiveFrom = request.EffectiveFrom!.Value
        };

        db.MaterialCosts.Add(cost);
        audit.Record(cost.TenantId, "materialcost.created", "MaterialCost", cost.Id.ToString(),
            new { cost.MaterialId, cost.PlantId, cost.CostPerUnit, cost.EffectiveFrom });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.MaterialCosts.AsNoTracking().Where(c => c.Id == cost.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = cost.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Update(Guid id, UpdateMaterialCostRequest request, CancellationToken cancellationToken)
    {
        var cost = await db.MaterialCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        if (request.CostPerUnit != Math.Round(request.CostPerUnit, 4))
        {
            return Problem(title: "The cost can have at most 4 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.EffectiveFrom != cost.EffectiveFrom
            && await db.MaterialCosts.AnyAsync(c =>
                c.Id != id && c.MaterialId == cost.MaterialId && c.PlantId == cost.PlantId && c.EffectiveFrom == request.EffectiveFrom,
                cancellationToken))
        {
            return Problem(title: "This material already has a cost from that date for that plant.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var before = new { cost.CostPerUnit, cost.EffectiveFrom };
        cost.CostPerUnit = request.CostPerUnit;
        cost.EffectiveFrom = request.EffectiveFrom!.Value;

        audit.Record(cost.TenantId, "materialcost.updated", "MaterialCost", cost.Id.ToString(),
            new { Before = before, After = new { cost.CostPerUnit, cost.EffectiveFrom } });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Nothing references a cost row yet, so a wrong one can simply be removed.
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var cost = await db.MaterialCosts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cost is null) return NotFound();

        db.MaterialCosts.Remove(cost);
        audit.Record(cost.TenantId, "materialcost.deleted", "MaterialCost", cost.Id.ToString(),
            new { cost.MaterialId, cost.PlantId, cost.CostPerUnit, cost.EffectiveFrom });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}