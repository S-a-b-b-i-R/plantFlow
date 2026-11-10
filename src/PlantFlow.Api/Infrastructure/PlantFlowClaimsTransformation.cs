using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Infrastructure;

// Runs after the Firebase token is validated. Finds (or creates) our user and adds our own claims.
public class PlantFlowClaimsTransformation(
    PlantFlowDbContext db, IConfiguration configuration, PermissionService permissions)
    : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return principal;

        // This can run more than once per request, so do nothing if our claims are already there.
        if (principal.HasClaim(c => c.Type == HttpContextCurrentUser.UserIdClaim)) return principal;

        var firebaseUid = principal.FindFirst("sub")?.Value;
        var email = principal.FindFirst("email")?.Value;
        var emailVerified = string.Equals(
            principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        // No usable identity: leave our claims out, so the default policy denies the request.
        if (firebaseUid is null || email is null || !emailVerified) return principal;

        var isBootstrapAdmin = string.Equals(
            email, configuration["Bootstrap:PlatformAdminEmail"], StringComparison.OrdinalIgnoreCase);

        // IgnoreQueryFilters: we don't know the tenant yet, we are finding it out.
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

        if (user is null)
        {
            user = await CreateUserAsync(firebaseUid, email, principal.FindFirst("name")?.Value, isBootstrapAdmin);
        }
        else if (isBootstrapAdmin && (!user.IsPlatformAdmin || user.Status != UserStatus.Active))
        {
            user.IsPlatformAdmin = true;
            user.Status = UserStatus.Active;
            user.ApprovedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(HttpContextCurrentUser.UserIdClaim, user.Id.ToString()));
        identity.AddClaim(new Claim(HttpContextCurrentUser.TenantIdClaim, user.TenantId.ToString()));
        identity.AddClaim(new Claim(PlantFlowClaims.Status, user.Status.ToString()));
        if (user.IsPlatformAdmin)
        {
            identity.AddClaim(new Claim(PlantFlowClaims.PlatformAdmin, "true"));
        }

        // Only Active users get permissions; everyone else gets none.
        if (user.Status == UserStatus.Active)
        {
            foreach (var permission in await permissions.GetEffectivePermissionsAsync(user.Id, user.TenantId))
            {
                identity.AddClaim(new Claim(PlantFlowClaims.Permission, permission));
            }
        }

        principal.AddIdentity(identity);
        return principal;
    }

    private async Task<User> CreateUserAsync(string firebaseUid, string email, string? name, bool isBootstrapAdmin)
    {
        var user = new User
        {
            TenantId = SystemTenant.Id,
            FirebaseUid = firebaseUid,
            Email = email,
            DisplayName = name,
            Status = isBootstrapAdmin ? UserStatus.Active : UserStatus.Pending,
            IsPlatformAdmin = isBootstrapAdmin,
            ApprovedAtUtc = isBootstrapAdmin ? DateTime.UtcNow : null
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
            return user;
        }
        catch (DbUpdateException)
        {
            // Two first requests raced and the other one created the user. Load that one instead.
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.IgnoreQueryFilters().FirstAsync(u => u.FirebaseUid == firebaseUid);
        }
    }
}