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

public record PlantSummaryDto(
    Guid Id, string Code, string Name, string? Address, string TimeZone,
    decimal OperatingHoursPerDay, ActiveStatus Status, int BatchingPlantCount, decimal DailyCapacityCft);

public record PlantDetailDto(PlantSummaryDto Plant, List<BatchingPlantDto> BatchingPlants);

public class PlantRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [StringLength(500)]
    public string? Address { get; init; }

    // IANA name, for example "Asia/Dhaka".
    [Required, StringLength(64)]
    public string TimeZone { get; init; } = "UTC";

    [Range(1, 24)]
    public decimal OperatingHoursPerDay { get; init; } = 10;

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/plants")]
public class PlantsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    // Daily capacity is derived, never stored:
    // sum of rated capacity per hour of the active batching plants, times operating hours.
    private static readonly Expression<Func<Plant, PlantSummaryDto>> ToSummary = p => new PlantSummaryDto(
        p.Id, p.Code, p.Name, p.Address, p.TimeZone, p.OperatingHoursPerDay, p.Status,
        p.BatchingPlants.Count(),
        p.BatchingPlants.Where(b => b.Status == ActiveStatus.Active).Sum(b => b.RatedCapacityCftPerHour)
            * p.OperatingHoursPerDay);

    // ?search=  matches code or name.  ?status=Active|Inactive.  ?sort=code|name, with "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<PlantSummaryDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Plants.AsNoTracking();

        if (status is not null) query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "-name" => query.OrderByDescending(p => p.Name),
            "-code" => query.OrderByDescending(p => p.Code),
            _ => query.OrderBy(p => p.Code)
        };

        return await query.Select(ToSummary).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PlantDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(id, cancellationToken);
        if (detail is null) return NotFound();
        return detail;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<PlantDetailDto>> Create(PlantRequest request, CancellationToken cancellationToken)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
        {
            return Problem(title: "Unknown time zone.", statusCode: StatusCodes.Status400BadRequest);
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Plants.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return Problem(title: "A plant with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = currentUser.TenantId!.Value;
        var plant = new Plant
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            TimeZone = request.TimeZone,
            OperatingHoursPerDay = request.OperatingHoursPerDay,
            Status = request.Status
        };

        db.Plants.Add(plant);
        audit.Record(tenantId, "plant.created", "Plant", plant.Id.ToString(), new { plant.Code, plant.Name });
        await db.SaveChangesAsync(cancellationToken);

        var detail = await LoadDetailAsync(plant.Id, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = plant.Id }, detail);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, PlantRequest request, CancellationToken cancellationToken)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
        {
            return Problem(title: "Unknown time zone.", statusCode: StatusCodes.Status400BadRequest);
        }

        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plant is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Plants.AnyAsync(p => p.Code == code && p.Id != id, cancellationToken))
        {
            return Problem(title: "A plant with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        plant.Code = code;
        plant.Name = request.Name.Trim();
        plant.Address = request.Address?.Trim();
        plant.TimeZone = request.TimeZone;
        plant.OperatingHoursPerDay = request.OperatingHoursPerDay;
        plant.Status = request.Status;

        audit.Record(plant.TenantId, "plant.updated", "Plant", plant.Id.ToString(),
            new { plant.Code, plant.Name, Status = plant.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<PlantDetailDto?> LoadDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var summary = await db.Plants.AsNoTracking()
            .Where(p => p.Id == id).Select(ToSummary).FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        var batchingPlants = await db.BatchingPlants.AsNoTracking()
            .Where(b => b.PlantId == id).OrderBy(b => b.Code)
            .Select(BatchingPlantsController.ToDto)
            .ToListAsync(cancellationToken);

        return new PlantDetailDto(summary, batchingPlants);
    }
}