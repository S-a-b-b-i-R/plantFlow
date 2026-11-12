using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record PlantShiftDto(
    Guid Id, Guid PlantId, string Name, TimeOnly StartTime, TimeOnly EndTime,
    bool CrossesMidnight, int DurationMinutes, ActiveStatus Status);

public class PlantShiftRequest
{
    [Required, StringLength(50, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Local time at the plant, for example "06:00". Whole minutes only.
    [Required]
    public TimeOnly? StartTime { get; init; }

    // At or before the start means the shift runs past midnight, for example 22:00 to 06:00.
    [Required]
    public TimeOnly? EndTime { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
public class PlantShiftsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static PlantShiftDto ToDto(PlantShift s) => new(
        s.Id, s.PlantId, s.Name, s.StartTime, s.EndTime, s.CrossesMidnight, s.DurationMinutes, s.Status);

    // A plant has a handful of shifts, so this returns them all, in the order they start during the day.
    [HttpGet("api/plants/{plantId:guid}/shifts")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<List<PlantShiftDto>>> List(Guid plantId, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken)) return NotFound();

        var shifts = await db.PlantShifts.AsNoTracking()
            .Where(s => s.PlantId == plantId)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return shifts.Select(ToDto).ToList();
    }

    [HttpPost("api/plants/{plantId:guid}/shifts")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<PlantShiftDto>> Create(
        Guid plantId, PlantShiftRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken)) return NotFound();

        var problem = await CheckAsync(plantId, request, null, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var shift = new PlantShift
        {
            TenantId = tenantId,
            PlantId = plantId,
            Name = request.Name.Trim(),
            StartTime = request.StartTime!.Value,
            EndTime = request.EndTime!.Value,
            Status = request.Status
        };

        db.PlantShifts.Add(shift);
        audit.Record(tenantId, "plantshift.created", "PlantShift", shift.Id.ToString(),
            new { shift.Name, shift.PlantId, Start = shift.StartTime.ToString("HH:mm"), End = shift.EndTime.ToString("HH:mm") });
        await db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ToDto(shift));
    }

    [HttpPut("api/plant-shifts/{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, PlantShiftRequest request, CancellationToken cancellationToken)
    {
        var shift = await db.PlantShifts.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null) return NotFound();

        var problem = await CheckAsync(shift.PlantId, request, id, cancellationToken);
        if (problem is not null) return problem;

        shift.Name = request.Name.Trim();
        shift.StartTime = request.StartTime!.Value;
        shift.EndTime = request.EndTime!.Value;
        shift.Status = request.Status;

        audit.Record(shift.TenantId, "plantshift.updated", "PlantShift", shift.Id.ToString(),
            new
            {
                shift.Name,
                Start = shift.StartTime.ToString("HH:mm"),
                End = shift.EndTime.ToString("HH:mm"),
                Status = shift.Status.ToString()
            });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckAsync(
        Guid plantId, PlantShiftRequest request, Guid? selfId, CancellationToken cancellationToken)
    {
        var start = request.StartTime!.Value;
        var end = request.EndTime!.Value;

        if (start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            return Problem(title: "Shift times must be whole minutes.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (start == end)
        {
            return Problem(title: "A shift must be longer than zero and shorter than 24 hours.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var name = request.Name.Trim();
        if (await db.PlantShifts.AnyAsync(s => s.PlantId == plantId && s.Name == name && s.Id != selfId, cancellationToken))
        {
            return Problem(title: "This plant already has a shift with that name.", statusCode: StatusCodes.Status409Conflict);
        }

        // Only active shifts must stay apart. Retired shifts can overlap, so a shift can be replaced.
        if (request.Status == ActiveStatus.Active)
        {
            var candidate = new PlantShift { PlantId = plantId, Name = name, StartTime = start, EndTime = end };

            var others = await db.PlantShifts.AsNoTracking()
                .Where(s => s.PlantId == plantId && s.Id != selfId && s.Status == ActiveStatus.Active)
                .ToListAsync(cancellationToken);

            var clash = others.FirstOrDefault(o => o.OverlapsWith(candidate));
            if (clash is not null)
            {
                return Problem(title: $"This shift overlaps the active shift '{clash.Name}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        return null;
    }
}