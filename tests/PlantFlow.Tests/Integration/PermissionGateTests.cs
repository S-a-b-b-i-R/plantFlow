using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class PermissionGateTests(ApiFactory factory)
{
    [Fact]
    public async Task Anonymous_requests_are_rejected_with_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pending_user_can_see_their_status_but_nothing_else()
    {
        var company = await factory.CreateCompanyAsync();
        var pending = await factory.AddMemberAsync(company, approve: false);

        var me = await pending.Client.GetAsync("/api/me");
        var users = await pending.Client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
    }

    [Fact]
    public async Task An_approved_user_without_the_permission_gets_403()
    {
        var company = await factory.CreateCompanyAsync();
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var response = await rep.Client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_company_admin_with_the_permission_gets_200()
    {
        var company = await factory.CreateCompanyAsync();

        var response = await company.Admin.Client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_company_admin_cannot_use_platform_endpoints()
    {
        var company = await factory.CreateCompanyAsync();

        var response = await company.Admin.Client.GetAsync("/api/platform/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_disabled_user_loses_access_immediately()
    {
        var company = await factory.CreateCompanyAsync();
        var secondAdmin = await factory.AddMemberAsync(company, DefaultRoles.AdminRoleName);
        Assert.Equal(HttpStatusCode.OK, (await secondAdmin.Client.GetAsync("/api/users")).StatusCode);

        (await company.Admin.Client.PostAsync($"/api/users/{secondAdmin.Id}/disable", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await secondAdmin.Client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task An_explicit_deny_beats_the_role_and_applies_at_once()
    {
        var company = await factory.CreateCompanyAsync();
        var secondAdmin = await factory.AddMemberAsync(company, DefaultRoles.AdminRoleName);

        (await company.Admin.Client.PutAsJsonAsync(
            $"/api/users/{secondAdmin.Id}/permissions",
            new { overrides = new[] { new { permissionKey = Permissions.UsersManage, isGranted = false } } }))
            .EnsureSuccessStatusCode();

        var response = await secondAdmin.Client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var me = await secondAdmin.Client.GetFromJsonAsync<MeResponse>("/api/me", ApiFactory.Json);
        Assert.DoesNotContain(Permissions.UsersManage, me!.Permissions);
    }

    [Fact]
    public async Task Admins_cannot_change_their_own_account()
    {
        var company = await factory.CreateCompanyAsync();

        var response = await company.Admin.Client.PostAsync($"/api/users/{company.Admin.Id}/disable", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}