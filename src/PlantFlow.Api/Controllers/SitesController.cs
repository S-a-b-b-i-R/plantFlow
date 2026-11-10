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

public record SiteDto(
    Guid Id, Guid CustomerId, string Name, string? Address, decimal? Latitude, decimal? Longitude,
    string? ContactName, string? ContactPhone, string? AccessNotes, ActiveStatus Status);

public class SiteRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [StringLength(500)]
    public string? Address { get; init; }

    // Give both or neither.
    [Range(-90.0, 90.0)]
    public decimal? Latitude { get; init; }

    [Range(-180.0, 180.0)]
    public decimal? Longitude { get; init; }

    [StringLength(200)]
    public string? ContactName { get; init; }

    [StringLength(30)]
    public string? ContactPhone { get; init; }

    [StringLength(1000)]
    public string? AccessNotes { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
public class SitesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Site, SiteDto>> ToDto = s => new SiteDto(
        s.Id, s.CustomerId, s.Name, s.Address, s.Latitude, s.Longitude,
        s.ContactName, s.ContactPhone, s.AccessNotes, s.Status);

    // ?search= matches name or address.  ?status=  ?sort=name, "-name" for descending.
    [HttpGet("api/customers/{customerId:guid}/sites")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<SiteDto>>> List(
        Guid customerId, [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status,
        CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken)) return NotFound();

        var query = db.Sites.AsNoTracking().Where(s => s.CustomerId == customerId);

        if (status is not null) query = query.Where(s => s.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(s => s.Name.Contains(search) || (s.Address != null && s.Address.Contains(search)));
        }

        query = paging.Sort?.ToLowerInvariant() == "-name" ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name);

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("api/sites/{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<SiteDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (site is null) return NotFound();
        return site;
    }

    [HttpPost("api/customers/{customerId:guid}/sites")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<SiteDto>> Create(Guid customerId, SiteRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken)) return NotFound();

        var problem = await CheckAsync(request, customerId, null, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var site = new Site
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ContactName = request.ContactName?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            AccessNotes = request.AccessNotes?.Trim(),
            Status = request.Status
        };

        db.Sites.Add(site);
        audit.Record(tenantId, "site.created", "Site", site.Id.ToString(), new { site.Name, site.CustomerId });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Sites.AsNoTracking().Where(s => s.Id == site.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = site.Id }, dto);
    }

    [HttpPut("api/sites/{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, SiteRequest request, CancellationToken cancellationToken)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (site is null) return NotFound();

        var problem = await CheckAsync(request, site.CustomerId, id, cancellationToken);
        if (problem is not null) return problem;

        site.Name = request.Name.Trim();
        site.Address = request.Address?.Trim();
        site.Latitude = request.Latitude;
        site.Longitude = request.Longitude;
        site.ContactName = request.ContactName?.Trim();
        site.ContactPhone = request.ContactPhone?.Trim();
        site.AccessNotes = request.AccessNotes?.Trim();
        site.Status = request.Status;

        audit.Record(site.TenantId, "site.updated", "Site", site.Id.ToString(),
            new { site.Name, Status = site.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckAsync(
        SiteRequest request, Guid customerId, Guid? selfId, CancellationToken cancellationToken)
    {
        if ((request.Latitude is null) != (request.Longitude is null))
        {
            return Problem(title: "Give both latitude and longitude, or neither.", statusCode: StatusCodes.Status400BadRequest);
        }

        var name = request.Name.Trim();
        if (await db.Sites.AnyAsync(s => s.CustomerId == customerId && s.Name == name && s.Id != selfId, cancellationToken))
        {
            return Problem(title: "This customer already has a site with that name.", statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
}