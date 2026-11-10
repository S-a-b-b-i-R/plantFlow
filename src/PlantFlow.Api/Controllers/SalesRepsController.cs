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

public record SalesRepDto(Guid Id, string Name, string? Region, Guid? UserId, Guid? ManagerId, ActiveStatus Status);

public class SalesRepRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [StringLength(100)]
    public string? Region { get; init; }

    // Optional link to a login user of this company.
    public Guid? UserId { get; init; }

    // Optional: the rep this one reports to.
    public Guid? ManagerId { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/sales-reps")]
public class SalesRepsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<SalesRep, SalesRepDto>> ToDto = r =>
        new SalesRepDto(r.Id, r.Name, r.Region, r.UserId, r.ManagerId, r.Status);

    // ?search= matches name or region.  ?status=.  ?sort=name|region, with "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<SalesRepDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, CancellationToken cancellationToken)
    {
        var query = db.SalesReps.AsNoTracking();

        if (status is not null) query = query.Where(r => r.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(r => r.Name.Contains(search) || (r.Region != null && r.Region.Contains(search)));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "-name" => query.OrderByDescending(r => r.Name),
            "region" => query.OrderBy(r => r.Region).ThenBy(r => r.Name),
            "-region" => query.OrderByDescending(r => r.Region).ThenBy(r => r.Name),
            _ => query.OrderBy(r => r.Name)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<SalesRepDto>> Create(SalesRepRequest request, CancellationToken cancellationToken)
    {
        var problem = await CheckLinksAsync(request, null, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var rep = new SalesRep
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Region = request.Region?.Trim(),
            UserId = request.UserId,
            ManagerId = request.ManagerId,
            Status = request.Status
        };

        db.SalesReps.Add(rep);
        audit.Record(tenantId, "salesrep.created", "SalesRep", rep.Id.ToString(), new { rep.Name });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.SalesReps.AsNoTracking().Where(r => r.Id == rep.Id).Select(ToDto).FirstAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, SalesRepRequest request, CancellationToken cancellationToken)
    {
        var rep = await db.SalesReps.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rep is null) return NotFound();

        var problem = await CheckLinksAsync(request, id, cancellationToken);
        if (problem is not null) return problem;

        rep.Name = request.Name.Trim();
        rep.Region = request.Region?.Trim();
        rep.UserId = request.UserId;
        rep.ManagerId = request.ManagerId;
        rep.Status = request.Status;

        audit.Record(rep.TenantId, "salesrep.updated", "SalesRep", rep.Id.ToString(),
            new { rep.Name, Status = rep.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Checks the user link and the manager link. selfId is null when creating.
    private async Task<ObjectResult?> CheckLinksAsync(SalesRepRequest request, Guid? selfId, CancellationToken cancellationToken)
    {
        if (request.UserId is { } userId)
        {
            // The tenant filter means a user of another company simply isn't found.
            if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            {
                return Problem(title: "That user does not exist in this company.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (await db.SalesReps.AnyAsync(r => r.UserId == userId && r.Id != selfId, cancellationToken))
            {
                return Problem(title: "That user is already linked to another sales rep.", statusCode: StatusCodes.Status409Conflict);
            }
        }

        if (request.ManagerId is { } managerId)
        {
            if (!await db.SalesReps.AnyAsync(r => r.Id == managerId, cancellationToken))
            {
                return Problem(title: "The manager does not exist in this company.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (selfId is { } self && await WouldCreateLoopAsync(self, managerId, cancellationToken))
            {
                return Problem(title: "A sales rep cannot report to themselves or to someone who reports to them.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return null;
    }

    // Walks up the manager chain from the proposed manager. Reaching this rep again means a loop.
    private async Task<bool> WouldCreateLoopAsync(Guid selfId, Guid managerId, CancellationToken cancellationToken)
    {
        Guid? current = managerId;
        for (var depth = 0; current is not null && depth < 100; depth++)
        {
            if (current == selfId) return true;

            var next = current;
            current = await db.SalesReps.Where(r => r.Id == next).Select(r => r.ManagerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }
}