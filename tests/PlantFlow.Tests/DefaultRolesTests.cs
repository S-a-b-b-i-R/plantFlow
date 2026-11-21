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
    public void Only_plant_managers_and_admins_set_up_plants()
    {
        var managers = DefaultRoles.All.Where(r => r.Value.Contains(Permissions.PlantsManage)).Select(r => r.Key);

        Assert.Equal(["Admin", "Plant Manager"], managers.Order());
    }

    [Fact]
    public void Sales_manages_customers_managers_assign_reps_and_accounts_sets_vat_exemption()
    {
        string[] Holders(string key) => DefaultRoles.All.Where(r => r.Value.Contains(key)).Select(r => r.Key).Order().ToArray();

        Assert.Equal(["Admin", "Sales Manager", "Sales Representative"], Holders(Permissions.CustomersManage));
        Assert.Equal(["Admin", "Sales Manager"], Holders(Permissions.CustomersAssign));
        Assert.Equal(["Accounts", "Admin"], Holders(Permissions.CustomersVat));
    }

    [Fact]
    public void Admin_role_has_every_permission()
    {
        Assert.Equal(Permissions.All.Order(), DefaultRoles.All["Admin"].Order());
    }
}