using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Api.Services;

public class PermissionService(PlantFlowDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    // Role permissions, plus the user's own grants, minus the user's explicit denies.
    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, Guid tenantId)
    {
        var key = CacheKey(userId, tenantId);
        if (cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null) return cached;

        var loaded = await LoadAsync(userId, tenantId);
        cache.Set(key, loaded, CacheDuration);
        return loaded;
    }

    // Call this whenever a user's roles or permissions change.
    public void Invalidate(Guid userId, Guid tenantId) => cache.Remove(CacheKey(userId, tenantId));

    private async Task<IReadOnlySet<string>> LoadAsync(Guid userId, Guid tenantId)
    {
        // IgnoreQueryFilters: this runs while the request is still being authenticated, before a tenant is known.
        var rolePermissions = await db.UserRoles.IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .SelectMany(ur => ur.Role.Permissions)
            .Select(rp => rp.PermissionKey)
            .ToListAsync();

        var overrides = await db.UserPermissions.IgnoreQueryFilters()
            .Where(up => up.UserId == userId && up.TenantId == tenantId)
            .ToListAsync();

        var effective = new HashSet<string>(rolePermissions);
        foreach (var grant in overrides.Where(o => o.IsGranted)) effective.Add(grant.PermissionKey);
        foreach (var deny in overrides.Where(o => !o.IsGranted)) effective.Remove(deny.PermissionKey);

        effective.IntersectWith(Permissions.All); // ignore keys we no longer know
        return effective;
    }

    private static string CacheKey(Guid userId, Guid tenantId) => $"permissions:{tenantId}:{userId}";
}