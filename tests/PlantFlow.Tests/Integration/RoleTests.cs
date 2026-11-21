using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Services;

namespace PlantFlow.Tests.Integration;

// Companies edit and rename roles and make their own. Admin is locked, built-in roles can't be deleted,
// and a role people still have can't be deleted.
[Collection("api")]
public class RoleTests(ApiFactory factory)
{
    private static async Task<List<RoleDto>> RolesAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<List<RoleDto>>("/api/roles", ApiFactory.Json))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_custom_role_takes_effect_at_once_and_follows_its_edits()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var created = await admin.PostAsJsonAsync("/api/roles",
            new { name = "Yard Supervisor", permissions = new[] { Permissions.MasterDataView } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = (await RolesAsync(admin)).Single(r => r.Name == "Yard Supervisor");
        Assert.False(role.IsBuiltIn);

        var member = await factory.AddMemberAsync(company, "Yard Supervisor");
        Assert.Equal(HttpStatusCode.OK, (await member.Client.GetAsync("/api/plants")).StatusCode);

        // Taking the permission away applies on the member's next request, not after the cache expires.
        (await admin.PutAsJsonAsync($"/api/roles/{role.Id}",
            new { name = "Yard Supervisor", permissions = new[] { Permissions.DashboardsView } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync("/api/plants")).StatusCode);

        await AssertProblemAsync(
            await admin.PostAsJsonAsync("/api/roles", new { name = "planner", permissions = Array.Empty<string>() }),
            HttpStatusCode.Conflict, RolesController.NameTaken);
        await AssertProblemAsync(
            await admin.PostAsJsonAsync("/api/roles", new { name = "Odd", permissions = new[] { "made.up" } }),
            HttpStatusCode.BadRequest, RolesController.UnknownPermission);
    }

    [Fact]
    public async Task The_admin_role_is_locked_and_always_has_every_permission()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var adminRole = (await RolesAsync(admin)).Single(r => r.Name == DefaultRoles.AdminRoleName);
        Assert.True(adminRole.IsLocked);
        Assert.Equal(Permissions.All.Order(), adminRole.Permissions);

        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/api/roles/{adminRole.Id}", new { name = "Boss", permissions = new[] { Permissions.OrdersView } }),
            HttpStatusCode.Conflict, RolesController.AdminLocked);
        await AssertProblemAsync(await admin.DeleteAsync($"/api/roles/{adminRole.Id}"),
            HttpStatusCode.Conflict, RolesController.BuiltInCannotBeDeleted);

        // A permission missing from the Admin role's rows (as when PlantFlow adds a new one) still applies.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
            await db.RolePermissions.IgnoreQueryFilters()
                .Where(p => p.RoleId == adminRole.Id && p.PermissionKey == Permissions.FleetManage)
                .ExecuteDeleteAsync();
        }
        var secondAdmin = await factory.AddMemberAsync(company, DefaultRoles.AdminRoleName);
        var me = await secondAdmin.Client.GetFromJsonAsync<MeResponse>("/api/me", ApiFactory.Json);
        Assert.Contains(Permissions.FleetManage, me!.Permissions);
    }

    [Fact]
    public async Task A_renamed_built_in_role_keeps_its_identity_and_cannot_be_deleted()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var planner = (await RolesAsync(admin)).Single(r => r.Name == "Planner");

        (await admin.PutAsJsonAsync($"/api/roles/{planner.Id}",
            new { name = "Shift Planner", permissions = planner.Permissions })).EnsureSuccessStatusCode();
        await AssertProblemAsync(await admin.DeleteAsync($"/api/roles/{planner.Id}"),
            HttpStatusCode.Conflict, RolesController.BuiltInCannotBeDeleted);

        // Seeding again (as on a new release) doesn't bring back a second "Planner".
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedDefaultRolesAsync(company.TenantId);
        }
        var roles = await RolesAsync(admin);
        Assert.DoesNotContain(roles, r => r.Name == "Planner");
        Assert.True(roles.Single(r => r.Name == "Shift Planner").IsBuiltIn);
        Assert.Equal(DefaultRoles.All.Count, roles.Count);
    }

    [Fact]
    public async Task A_custom_role_can_be_deleted_only_when_nobody_has_it()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        (await admin.PostAsJsonAsync("/api/roles", new { name = "Temp", permissions = Array.Empty<string>() }))
            .EnsureSuccessStatusCode();
        var temp = (await RolesAsync(admin)).Single(r => r.Name == "Temp");
        var member = await factory.AddMemberAsync(company, "Temp");

        await AssertProblemAsync(await admin.DeleteAsync($"/api/roles/{temp.Id}"),
            HttpStatusCode.Conflict, "1 person has this role. Give them another role first.");

        (await admin.PutAsJsonAsync($"/api/users/{member.Id}/roles", new { roleIds = Array.Empty<Guid>() }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{temp.Id}")).StatusCode);
        Assert.DoesNotContain(await RolesAsync(admin), r => r.Name == "Temp");
    }

    [Fact]
    public async Task Only_role_managers_change_roles_and_other_companies_roles_stay_hidden()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var planner = await factory.AddMemberAsync(company, "Planner");
        var otherRole = (await RolesAsync(other.Admin.Client)).Single(r => r.Name == "Planner");
        var body = new { name = "Renamed", permissions = Array.Empty<string>() };

        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.PostAsJsonAsync("/api/roles", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await company.Admin.Client.PutAsJsonAsync($"/api/roles/{otherRole.Id}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await company.Admin.Client.DeleteAsync($"/api/roles/{otherRole.Id}")).StatusCode);
    }
}
