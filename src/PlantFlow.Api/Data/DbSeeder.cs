using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.Tenancy;

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
}