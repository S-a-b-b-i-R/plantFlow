using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Security;

namespace PlantFlow.Tests.Integration;

// Disabling needs a reason, the company always keeps one active Admin, and pending sign-ups are counted for the menu badge.
[Collection("api")]
public class UserAdminTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> DisableAsync(HttpClient c, Guid userId, string? reason) =>
        reason is null
            ? c.PostAsync($"/api/users/{userId}/disable", null)
            : c.PostAsJsonAsync($"/api/users/{userId}/disable", new { reason });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabling_a_user_needs_a_reason_that_is_audited()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var planner = await factory.AddMemberAsync(company, "Planner");

        await AssertProblemAsync(await DisableAsync(admin, planner.Id, null), HttpStatusCode.BadRequest, UsersController.ReasonNeeded);
        await AssertProblemAsync(await DisableAsync(admin, planner.Id, "  "), HttpStatusCode.BadRequest, UsersController.ReasonNeeded);

        Assert.Equal(HttpStatusCode.NoContent, (await DisableAsync(admin, planner.Id, "Left the company")).StatusCode);
        var audit = await TestData.AuditAsync(admin);
        Assert.Contains(audit!, e => e.Action == "user.disabled" && e.ChangesJson!.Contains("Left the company"));

        // Enabling again is one step and needs no reason.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{planner.Id}/enable", null)).StatusCode);
    }

    [Fact]
    public async Task The_only_active_admin_cannot_be_disabled_demoted_or_denied_user_management()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin;
        // Someone who manages users without being an Admin, so they can try to change the only Admin.
        var helper = await factory.AddMemberAsync(company, "Planner");
        (await admin.Client.PutAsJsonAsync($"/api/users/{helper.Id}/permissions",
            new { overrides = new[] { new { permissionKey = Permissions.UsersManage, isGranted = true } } }))
            .EnsureSuccessStatusCode();
        var roles = (await helper.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles", ApiFactory.Json))!;
        var plannerRole = roles.Single(r => r.Name == "Planner").Id;

        await AssertProblemAsync(await DisableAsync(helper.Client, admin.Id, "Testing"),
            HttpStatusCode.Conflict, UsersController.LastAdminMessage);
        await AssertProblemAsync(
            await helper.Client.PutAsJsonAsync($"/api/users/{admin.Id}/roles", new { roleIds = new[] { plannerRole } }),
            HttpStatusCode.Conflict, UsersController.LastAdminMessage);
        await AssertProblemAsync(
            await helper.Client.PutAsJsonAsync($"/api/users/{admin.Id}/permissions",
                new { overrides = new[] { new { permissionKey = Permissions.UsersManage, isGranted = false } } }),
            HttpStatusCode.Conflict, UsersController.LastAdminMessage);

        // With a second active Admin, the first one can be disabled.
        await factory.AddMemberAsync(company, DefaultRoles.AdminRoleName);
        Assert.Equal(HttpStatusCode.NoContent, (await DisableAsync(helper.Client, admin.Id, "Handing over")).StatusCode);
    }

    [Fact]
    public async Task Pending_sign_ups_are_counted_for_admins_of_that_company_only()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        await factory.AddMemberAsync(company, approve: false);
        await factory.AddMemberAsync(company, approve: false);
        await factory.AddMemberAsync(other, approve: false);
        var planner = await factory.AddMemberAsync(company, "Planner");

        var count = await company.Admin.Client.GetFromJsonAsync<PendingCountDto>("/api/users/pending-count", ApiFactory.Json);
        Assert.Equal(2, count!.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/users/pending-count")).StatusCode);
    }
}
