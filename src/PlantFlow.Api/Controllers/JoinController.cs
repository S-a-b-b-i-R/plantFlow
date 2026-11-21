using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public class JoinRequest
{
    [Required, StringLength(20)]
    public string Code { get; init; } = "";
}

public record JoinedDto(string CompanyName);

public record JoinCodeDto(string Code, DateTime? RenewedAtUtc, JoinMethod Method);

// How people get into a company. Someone who has signed in but has no company enters the company's join
// code and waits for its Admin to approve them. While waiting they can leave and try another code.
[ApiController]
public class JoinController(
    PlantFlowDbContext db,
    ICurrentUser currentUser,
    AuditWriter audit,
    JoinCodes joinCodes,
    PermissionService permissionService,
    IMemoryCache cache) : ControllerBase
{
    public const int MaxWrongCodes = 5;
    public static readonly TimeSpan WrongCodeWindow = TimeSpan.FromMinutes(15);

    public const string WrongCode = "That code doesn't match a company. Check it with your company's administrator.";
    public const string TooManyWrongCodes = "Too many wrong codes. Wait 15 minutes and try again.";
    public const string CompanyPaused = "That company's access is paused. Ask your company's administrator.";
    public const string PlatformAdminsDontJoin = "Platform admins don't join companies.";
    public const string EmailTaken = "Someone with your email address already belongs to that company. Ask its administrator.";
    public const string OnlyWhileWaiting = "Only someone still waiting for approval can leave a company.";

    [HttpPost("api/join")]
    [Authorize(Policy = Policies.AuthenticatedUser)]
    public async Task<ActionResult<JoinedDto>> Join(JoinRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Forbid();
        if (user.IsPlatformAdmin) return Problem(title: PlatformAdminsDontJoin, statusCode: 409);
        if (user.TenantId != SystemTenant.Id)
        {
            var current = await db.Tenants.AsNoTracking().Where(t => t.Id == user.TenantId).Select(t => t.Name)
                .FirstAsync(cancellationToken);
            return Problem(title: user.Status == UserStatus.Pending
                ? $"You are already waiting to join {current}. Leave it first to use another code."
                : $"You already belong to {current}.", statusCode: 409);
        }

        // Wrong guesses are limited per person, so codes can't be found by trying.
        var failKey = $"join-fails:{user.Id}";
        var fails = cache.Get<int>(failKey);
        if (fails >= MaxWrongCodes) return Problem(title: TooManyWrongCodes, statusCode: 429);

        var code = JoinCodes.Normalize(request.Code);
        var tenant = code.Length == JoinCodes.Length
            ? await db.Tenants.FirstOrDefaultAsync(
                t => t.JoinCode == code && t.JoinMethod == JoinMethod.Code && t.Id != SystemTenant.Id, cancellationToken)
            : null;
        if (tenant is null)
        {
            cache.Set(failKey, fails + 1, WrongCodeWindow);
            return Problem(title: WrongCode, statusCode: 400);
        }
        if (tenant.Status != TenantStatus.Active) return Problem(title: CompanyPaused, statusCode: 409);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenant.Id && u.Email == user.Email, cancellationToken))
        {
            return Problem(title: EmailTaken, statusCode: 409);
        }

        user.TenantId = tenant.Id;
        user.Status = UserStatus.Pending;
        user.JoinedVia = JoinedVia.Code;
        user.ApprovedBy = null;
        user.ApprovedAtUtc = null;
        audit.Record(tenant.Id, "user.joined", "User", user.Id.ToString(), new { user.Email, Via = "Code" });
        await db.SaveChangesAsync(cancellationToken);

        cache.Remove(failKey);
        permissionService.Invalidate(user.Id, SystemTenant.Id);
        permissionService.Invalidate(user.Id, tenant.Id);
        return new JoinedDto(tenant.Name);
    }

    // Back to "no company", so the person can enter another code. Only while still waiting.
    [HttpPost("api/join/leave")]
    [Authorize(Policy = Policies.AuthenticatedUser)]
    public async Task<IActionResult> Leave(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Forbid();
        if (user.TenantId == SystemTenant.Id || user.Status != UserStatus.Pending)
        {
            return Problem(title: OnlyWhileWaiting, statusCode: 409);
        }

        var tenantId = user.TenantId;
        db.UserRoles.RemoveRange(await db.UserRoles.IgnoreQueryFilters().Where(r => r.UserId == user.Id).ToListAsync(cancellationToken));
        db.UserPermissions.RemoveRange(await db.UserPermissions.IgnoreQueryFilters().Where(p => p.UserId == user.Id).ToListAsync(cancellationToken));
        db.UserPlants.RemoveRange(await db.UserPlants.IgnoreQueryFilters().Where(p => p.UserId == user.Id).ToListAsync(cancellationToken));
        user.TenantId = SystemTenant.Id;
        user.JoinedVia = JoinedVia.Assigned;
        audit.Record(tenantId, "user.left", "User", user.Id.ToString(), new { user.Email });
        await db.SaveChangesAsync(cancellationToken);

        permissionService.Invalidate(user.Id, tenantId);
        permissionService.Invalidate(user.Id, SystemTenant.Id);
        return NoContent();
    }

    // The company's code, for an Admin to pass on. A company that never had one gets one now.
    [HttpGet("api/join-code")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<ActionResult<JoinCodeDto>> GetCode(CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, cancellationToken);
        if (tenant.JoinCode is null)
        {
            await joinCodes.RenewAsync(tenant, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        return new JoinCodeDto(JoinCodes.Format(tenant.JoinCode!), tenant.JoinCodeRenewedUtc, tenant.JoinMethod);
    }

    // A new code. The old one stops working at once; people already in the company are not affected.
    [HttpPost("api/join-code/renew")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<ActionResult<JoinCodeDto>> RenewCode(CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, cancellationToken);
        await joinCodes.RenewAsync(tenant, cancellationToken);
        audit.Record(tenant.Id, "join-code.renewed", "Tenant", tenant.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return new JoinCodeDto(JoinCodes.Format(tenant.JoinCode!), tenant.JoinCodeRenewedUtc, tenant.JoinMethod);
    }

    private Task<User?> CurrentUserAsync(CancellationToken cancellationToken) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
}
