using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Controllers;

public record MeResponse(
    Guid Id, string Email, string? DisplayName, string Status,
    bool IsPlatformAdmin, Guid TenantId, string TenantName, string TenantSlug);

[ApiController]
[Route("api/me")]
// Open to any signed-in user, including Pending ones, so the frontend can show "waiting for approval".
[Authorize(Policy = Policies.AuthenticatedUser)]
public class MeController(PlantFlowDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) return Forbid();

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null) return NotFound();

        return new MeResponse(
            user.Id, user.Email, user.DisplayName, user.Status.ToString(),
            user.IsPlatformAdmin, user.TenantId, user.Tenant.Name, user.Tenant.Slug);
    }
}