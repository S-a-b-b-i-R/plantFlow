using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record SiteDistanceDto(Guid PlantId, string PlantCode, decimal DistanceKm);

public class SiteDistanceRequest
{
    [Required]
    public Guid? PlantId { get; init; }

    // Km from the plant to the site. At most 2 decimals.
    [Range(0.0, 10000.0)]
    public decimal DistanceKm { get; init; }
}

public class SetSiteDistancesRequest
{
    // Replaces the whole list. A plant that is not listed has no stored distance.
    [Required]
    public List<SiteDistanceRequest> Distances { get; init; } = [];
}

[ApiController]
[Route("api/sites/{siteId:guid}/distances")]
public class SiteDistancesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<List<SiteDistanceDto>>> List(Guid siteId, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        return await db.SitePlantDistances.AsNoTracking()
            .Where(d => d.SiteId == siteId)
            .OrderBy(d => d.Plant.Code)
            .Select(d => new SiteDistanceDto(d.PlantId, d.Plant.Code, d.DistanceKm))
            .ToListAsync(cancellationToken);
    }

    [HttpPut]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Set(Guid siteId, SetSiteDistancesRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        var plantIds = request.Distances.Select(d => d.PlantId!.Value).ToList();

        if (plantIds.Distinct().Count() != plantIds.Count)
        {
            return Problem(title: "Each plant can appear only once.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Distances.Any(d => d.DistanceKm != Math.Round(d.DistanceKm, 2)))
        {
            return Problem(title: "Distances can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        var activePlants = await db.Plants.CountAsync(p => plantIds.Contains(p.Id) && p.Status == ActiveStatus.Active, cancellationToken);
        if (activePlants != plantIds.Count)
        {
            return Problem(title: "Every plant must exist in this company and be active.", statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await db.SitePlantDistances.Where(d => d.SiteId == siteId).ToListAsync(cancellationToken);
        var byPlant = existing.ToDictionary(d => d.PlantId);

        foreach (var wanted in request.Distances)
        {
            if (byPlant.TryGetValue(wanted.PlantId!.Value, out var current))
            {
                current.DistanceKm = wanted.DistanceKm;
            }
            else
            {
                db.SitePlantDistances.Add(new SitePlantDistance
                {
                    TenantId = currentUser.TenantId!.Value,
                    SiteId = siteId,
                    PlantId = wanted.PlantId.Value,
                    DistanceKm = wanted.DistanceKm
                });
            }
        }

        db.SitePlantDistances.RemoveRange(existing.Where(d => !plantIds.Contains(d.PlantId)));

        audit.Record(currentUser.TenantId!.Value, "site.distances_set", "Site", siteId.ToString(),
            request.Distances.Select(d => new { d.PlantId, d.DistanceKm }));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}