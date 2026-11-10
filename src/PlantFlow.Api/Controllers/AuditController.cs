using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Controllers;

public record AuditLogDto(
    Guid Id, DateTime OccurredAtUtc, Guid? UserId, string Action, string EntityType, string EntityId, string? ChangesJson);

[ApiController]
[Route("api/audit")]
[HasPermission(Permissions.AuditView)]
public class AuditController(PlantFlowDbContext db) : ControllerBase
{
    // Newest first. Proper paging comes with the shared list helper later.
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> List(
        [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        return await db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(take)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAtUtc, a.UserId, a.Action, a.EntityType, a.EntityId, a.ChangesJson))
            .ToListAsync(cancellationToken);
    }
}