using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Data;

// Development only: moves the bootstrap platform admin into the demo company as its Admin,
// so the web app has real data to show. The user has to have signed in once already.
public static class DemoAccessSeeder
{
    public static async Task EnsureAdminInDemoCompanyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IConfiguration>()["Bootstrap:PlatformAdminEmail"];
        if (string.IsNullOrWhiteSpace(email)) return;

        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == SystemTenant.Id);
        if (user is null) return; // Not signed in yet, or already moved.

        var adminRole = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(
            r => r.TenantId == DemoTenant.Id && r.SystemKey == DefaultRoles.AdminKey);
        if (adminRole is null) return;

        user.TenantId = DemoTenant.Id;
        user.Status = UserStatus.Active;
        user.ApprovedAtUtc ??= DateTime.UtcNow;
        db.UserRoles.Add(new UserRole { TenantId = DemoTenant.Id, UserId = user.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync();
    }
}