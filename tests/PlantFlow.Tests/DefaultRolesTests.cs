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
    public void Only_plant_managers_and_admins_approve_site_surveys_and_plant_managers_can_see_them()
    {
        var approvers = DefaultRoles.All.Where(r => r.Value.Contains(Permissions.SiteSurveysApprove)).Select(r => r.Key);

        Assert.Equal(["Admin", "Plant Manager"], approvers.Order());
        Assert.Contains(Permissions.SiteSurveysView, DefaultRoles.All["Plant Manager"]);
    }

    [Fact]
    public void Admin_role_has_every_permission()
    {
        Assert.Equal(Permissions.All.Order(), DefaultRoles.All["Admin"].Order());
    }
}