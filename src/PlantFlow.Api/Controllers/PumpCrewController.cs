using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public class PumpCrewRequest
{
    // The whole labour team, by name. It replaces the current list.
    [Required, MaxLength(20)]
    public List<string> Names { get; init; } = [];
}

[ApiController]
[Route("api/vehicles/{id:guid}/crew")]
public class PumpCrewController(PlantFlowDbContext db, AuditWriter audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<List<string>>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == id, cancellationToken)) return NotFound();

        return await db.PumpCrewMembers.AsNoTracking().Where(c => c.VehicleId == id)
            .OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(cancellationToken);
    }

    [HttpPut]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Replace(Guid id, PumpCrewRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null) return NotFound();

        if (vehicle.Type != VehicleType.Pump)
        {
            return Problem(title: "Only a pump has a labour crew.", statusCode: StatusCodes.Status400BadRequest);
        }

        var names = request.Names.Select(n => n.Trim()).ToList();
        if (names.Any(n => n.Length < 2 || n.Length > 100)
            || names.Select(n => n.ToLowerInvariant()).Distinct().Count() != names.Count)
        {
            return Problem(title: "Each name needs 2 to 100 characters, and no name can be listed twice.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var current = await db.PumpCrewMembers.Where(c => c.VehicleId == id).ToListAsync(cancellationToken);
        var removed = current.Where(c => !names.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var added = names.Where(n => !current.Any(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();

        db.PumpCrewMembers.RemoveRange(removed);
        foreach (var name in added)
        {
            db.PumpCrewMembers.Add(new PumpCrewMember { TenantId = vehicle.TenantId, VehicleId = id, Name = name });
        }

        audit.Record(vehicle.TenantId, "pumpcrew.replaced", "Vehicle", id.ToString(),
            new { vehicle.Code, Added = added, Removed = removed.Select(c => c.Name) });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}