using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record BatchingPlantDto(
    Guid Id, Guid PlantId, string Code, string? Make, string? Model,
    decimal RatedCapacityCftPerHour, int BatchDurationMinutes, ActiveStatus Status);

public class BatchingPlantRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [StringLength(100)]
    public string? Make { get; init; }

    [StringLength(100)]
    public string? Model { get; init; }

    [Range(0.01, 100000)]
    public decimal RatedCapacityCftPerHour { get; init; }

    [Range(1, 60)]
    public int BatchDurationMinutes { get; init; } = 8;

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
public class BatchingPlantsController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    public static readonly Expression<Func<BatchingPlant, BatchingPlantDto>> ToDto = b => new BatchingPlantDto(
        b.Id, b.PlantId, b.Code, b.Make, b.Model, b.RatedCapacityCftPerHour, b.BatchDurationMinutes, b.Status);

    // A plant has one to three batching plants, so this returns them all instead of paging.
    [HttpGet("api/plants/{plantId:guid}/batching-plants")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<List<BatchingPlantDto>>> List(Guid plantId, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken)) return NotFound();

        return await db.BatchingPlants.AsNoTracking()
            .Where(b => b.PlantId == plantId)
            .OrderBy(b => b.Code)
            .Select(ToDto)
            .ToListAsync(cancellationToken);
    }

    [HttpPost("api/plants/{plantId:guid}/batching-plants")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<BatchingPlantDto>> Create(
        Guid plantId, BatchingPlantRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken)) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.BatchingPlants.AnyAsync(b => b.PlantId == plantId && b.Code == code, cancellationToken))
        {
            return Problem(title: "This plant already has a batching plant with that code.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = currentUser.TenantId!.Value;
        var batchingPlant = new BatchingPlant
        {
            TenantId = tenantId,
            PlantId = plantId,
            Code = code,
            Make = request.Make?.Trim(),
            Model = request.Model?.Trim(),
            RatedCapacityCftPerHour = request.RatedCapacityCftPerHour,
            BatchDurationMinutes = request.BatchDurationMinutes,
            Status = request.Status
        };

        db.BatchingPlants.Add(batchingPlant);
        audit.Record(tenantId, "batchingplant.created", "BatchingPlant", batchingPlant.Id.ToString(),
            new { batchingPlant.Code, batchingPlant.PlantId, batchingPlant.RatedCapacityCftPerHour });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.BatchingPlants.AsNoTracking()
            .Where(b => b.Id == batchingPlant.Id).Select(ToDto).FirstAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpPut("api/batching-plants/{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, BatchingPlantRequest request, CancellationToken cancellationToken)
    {
        var batchingPlant = await db.BatchingPlants.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batchingPlant is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.BatchingPlants.AnyAsync(
                b => b.PlantId == batchingPlant.PlantId && b.Code == code && b.Id != id, cancellationToken))
        {
            return Problem(title: "This plant already has a batching plant with that code.",
                statusCode: StatusCodes.Status409Conflict);
        }

        batchingPlant.Code = code;
        batchingPlant.Make = request.Make?.Trim();
        batchingPlant.Model = request.Model?.Trim();
        batchingPlant.RatedCapacityCftPerHour = request.RatedCapacityCftPerHour;
        batchingPlant.BatchDurationMinutes = request.BatchDurationMinutes;
        batchingPlant.Status = request.Status;

        audit.Record(batchingPlant.TenantId, "batchingplant.updated", "BatchingPlant", batchingPlant.Id.ToString(),
            new { batchingPlant.Code, batchingPlant.RatedCapacityCftPerHour, Status = batchingPlant.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}