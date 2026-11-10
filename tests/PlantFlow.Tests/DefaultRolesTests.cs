using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Tests;

public class DefaultRolesTests
{
    [Fact]
    public void Permission_keys_are_unique()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void Every_default_role_uses_only_known_permissions()
    {
        foreach (var (role, permissions) in DefaultRoles.All)
        {
            foreach (var key in permissions)
            {
                Assert.True(Permissions.All.Contains(key), $"Role '{role}' uses unknown permission '{key}'.");
            }
        }
    }

    [Fact]
    public void Admin_role_has_every_permission()
    {
        Assert.Equal(Permissions.All.Order(), DefaultRoles.All["Admin"].Order());
    }
}