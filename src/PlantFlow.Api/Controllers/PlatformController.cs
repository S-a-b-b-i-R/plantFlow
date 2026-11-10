using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record TenantSummaryDto(Guid Id, string Name, string Slug, TenantStatus Status, int UserCount);

public record UnassignedUserDto(
    Guid Id, string Email, string? DisplayName, UserStatus Status, bool IsPlatformAdmin, DateTime CreatedAtUtc);

public class CreateTenantRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Lowercase letters, digits and single hyphens, for example "acme-concrete".
    [Required, StringLength(60, MinimumLength = 3), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")]
    public string Slug { get; init; } = "";
}

public class AssignUserRequest
{
    public Guid TenantId { get; init; }

    // True for a company's first admin: the user becomes Active with the Admin role straight away.
    public bool AsAdmin { get; init; }
}

[ApiController]
[Route("api/platform")]
[Authorize(Policy = Policies.PlatformAdmin)]
public class PlatformController(
    PlantFlowDbContext db,
    ICurrentUser currentUser,
    RoleSeeder roleSeeder,
    PermissionService permissionService,
    AuditWriter audit) : ControllerBase
{
    [HttpGet("tenants")]
    public async Task<ActionResult<List<TenantSummaryDto>>> GetTenants(CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.Id != SystemTenant.Id)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var counts = await db.Users.IgnoreQueryFilters()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        return tenants
            .Select(t => new TenantSummaryDto(t.Id, t.Name, t.Slug, t.Status, counts.GetValueOrDefault(t.Id)))
            .ToList();
    }

    [HttpPost("tenants")]
    public async Task<ActionResult<TenantSummaryDto>> CreateTenant(
        CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (request.Slug == SystemTenant.Slug || await db.Tenants.AnyAsync(t => t.Slug == request.Slug, cancellationToken))
        {
            return Problem(title: "That slug is already taken.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenant = new Tenant { Name = request.Name.Trim(), Slug = request.Slug };
        db.Tenants.Add(tenant);
        audit.Record(tenant.Id, "tenant.created", "Tenant", tenant.Id.ToString(), new { tenant.Name, tenant.Slug });
        await db.SaveChangesAsync(cancellationToken);

        await roleSeeder.SeedDefaultRolesAsync(tenant.Id, cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new TenantSummaryDto(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, 0));
    }

    // Everyone who has signed in but has not been given a company yet.
    [HttpGet("users/unassigned")]
    public async Task<ActionResult<List<UnassignedUserDto>>> GetUnassignedUsers(CancellationToken cancellationToken)
    {
        return await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == SystemTenant.Id)
            .OrderByDescending(u => u.CreatedAtUtc)
            .Select(u => new UnassignedUserDto(
                u.Id, u.Email, u.DisplayName, u.Status, u.IsPlatformAdmin, u.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    [HttpPost("users/{userId:guid}/assign")]
    public async Task<IActionResult> AssignUser(
        Guid userId, AssignUserRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.TenantId == SystemTenant.Id)
        {
            return Problem(title: "Choose a company.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Problem(title: "User not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (user.TenantId != SystemTenant.Id)
        {
            return Problem(title: "This user already belongs to a company.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Problem(title: "Company not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (tenant.Status != TenantStatus.Active)
        {
            return Problem(title: "That company is not active.", statusCode: StatusCodes.Status409Conflict);
        }

        user.TenantId = tenant.Id;

        if (request.AsAdmin)
        {
            var adminRole = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(
                r => r.TenantId == tenant.Id && r.Name == DefaultRoles.AdminRoleName, cancellationToken);

            if (adminRole is null)
            {
                return Problem(title: "The company has no Admin role.", statusCode: StatusCodes.Status409Conflict);
            }

            user.Status = UserStatus.Active;
            user.ApprovedBy = currentUser.UserId;
            user.ApprovedAtUtc = DateTime.UtcNow;
            db.UserRoles.Add(new UserRole { TenantId = tenant.Id, UserId = user.Id, RoleId = adminRole.Id });
        }

        audit.Record(tenant.Id, "user.assigned", "User", user.Id.ToString(),
            new { user.Email, TenantName = tenant.Name, request.AsAdmin });
        await db.SaveChangesAsync(cancellationToken);

        permissionService.Invalidate(user.Id, SystemTenant.Id);
        permissionService.Invalidate(user.Id, tenant.Id);
        return NoContent();
    }
}