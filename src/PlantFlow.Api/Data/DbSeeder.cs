using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Data;

public static class DbSeeder
{
    public static async Task EnsurePlatformTenantAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();

        if (await db.Tenants.AnyAsync(t => t.Id == SystemTenant.Id)) return;

        db.Tenants.Add(new Tenant { Id = SystemTenant.Id, Name = "Platform", Slug = SystemTenant.Slug });
        await db.SaveChangesAsync();
    }

    // Development only: a demo company with the default roles, so there is something to sign in to.
    public static async Task EnsureDemoTenantAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var roleSeeder = scope.ServiceProvider.GetRequiredService<RoleSeeder>();

        if (!await db.Tenants.AnyAsync(t => t.Id == DemoTenant.Id))
        {
            db.Tenants.Add(new Tenant
            {
                Id = DemoTenant.Id,
                Name = "Demo Ready-Mix Co.",
                Slug = DemoTenant.Slug,
                PumpFreeAboveCft = 1000m // demo value only
            });
            await db.SaveChangesAsync();
        }

        await roleSeeder.SeedDefaultRolesAsync(DemoTenant.Id);
    }
}