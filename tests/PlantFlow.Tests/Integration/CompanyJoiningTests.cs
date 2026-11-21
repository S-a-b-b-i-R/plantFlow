using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Services;

namespace PlantFlow.Tests.Integration;

// People join a company with its code and wait for its Admin. The platform admin can suspend a company.
[Collection("api")]
public class CompanyJoiningTests(ApiFactory factory)
{
    private static async Task<string> CodeAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<JoinCodeDto>("/api/join-code", ApiFactory.Json))!.Code;

    private static Task<HttpResponseMessage> JoinAsync(HttpClient c, string code) =>
        c.PostAsJsonAsync("/api/join", new { code });

    private static async Task<MeResponse> MeAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<MeResponse>("/api/me", ApiFactory.Json))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_new_person_joins_with_the_code_however_they_type_it_and_waits_for_the_admin()
    {
        var company = await factory.CreateCompanyAsync();
        var code = await CodeAsync(company.Admin.Client);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        var person = await factory.SignUpAsync();
        Assert.False((await MeAsync(person.Client)).HasCompany);

        var typed = " " + code.Replace("-", " ").ToLowerInvariant() + " ";
        var joined = await JoinAsync(person.Client, typed);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var me = await MeAsync(person.Client);
        Assert.True(me.HasCompany);
        Assert.Equal(company.TenantId, me.TenantId);
        Assert.Equal("Pending", me.Status);
        Assert.Empty(me.Permissions);

        // The Admin sees them waiting and approves them as usual.
        var pending = await company.Admin.Client.GetFromJsonAsync<PendingCountDto>("/api/users/pending-count", ApiFactory.Json);
        Assert.Equal(1, pending!.Count);
        Assert.Contains(await TestData.AuditAsync(company.Admin.Client), e => e.Action == "user.joined");
        Assert.Equal(HttpStatusCode.NoContent,
            (await company.Admin.Client.PostAsync($"/api/users/{person.Id}/approve", null)).StatusCode);

        // Once in a company, a code can't move them.
        var other = await factory.CreateCompanyAsync();
        await AssertProblemAsync(await JoinAsync(person.Client, await CodeAsync(other.Admin.Client)),
            HttpStatusCode.Conflict, "You already belong to");
    }

    [Fact]
    public async Task Someone_waiting_can_leave_and_use_another_code_but_not_after_approval()
    {
        var wrong = await factory.CreateCompanyAsync();
        var right = await factory.CreateCompanyAsync();
        var person = await factory.SignUpAsync();

        (await JoinAsync(person.Client, await CodeAsync(wrong.Admin.Client))).EnsureSuccessStatusCode();
        await AssertProblemAsync(await JoinAsync(person.Client, await CodeAsync(right.Admin.Client)),
            HttpStatusCode.Conflict, "Leave it first");

        Assert.Equal(HttpStatusCode.NoContent, (await person.Client.PostAsync("/api/join/leave", null)).StatusCode);
        Assert.False((await MeAsync(person.Client)).HasCompany);
        Assert.Contains(await TestData.AuditAsync(wrong.Admin.Client), e => e.Action == "user.left");

        (await JoinAsync(person.Client, await CodeAsync(right.Admin.Client))).EnsureSuccessStatusCode();
        (await right.Admin.Client.PostAsync($"/api/users/{person.Id}/approve", null)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await person.Client.PostAsync("/api/join/leave", null),
            HttpStatusCode.Conflict, JoinController.OnlyWhileWaiting);
    }

    [Fact]
    public async Task Wrong_codes_are_refused_and_limited()
    {
        var company = await factory.CreateCompanyAsync();
        var person = await factory.SignUpAsync();

        for (var i = 0; i < JoinController.MaxWrongCodes; i++)
        {
            await AssertProblemAsync(await JoinAsync(person.Client, "AAAA-AAAA"), HttpStatusCode.BadRequest, JoinController.WrongCode);
        }
        // Even the right code is refused until the wait is over.
        await AssertProblemAsync(await JoinAsync(person.Client, await CodeAsync(company.Admin.Client)),
            (HttpStatusCode)429, JoinController.TooManyWrongCodes);
    }

    [Fact]
    public async Task Renewing_the_code_stops_the_old_one()
    {
        var company = await factory.CreateCompanyAsync();
        var old = await CodeAsync(company.Admin.Client);
        var renewed = await company.Admin.Client.PostAsync("/api/join-code/renew", null);
        var code = (await renewed.Content.ReadFromJsonAsync<JoinCodeDto>(ApiFactory.Json))!.Code;
        Assert.NotEqual(old, code);
        Assert.Equal(code, await CodeAsync(company.Admin.Client));
        Assert.Contains(await TestData.AuditAsync(company.Admin.Client), e => e.Action == "join-code.renewed");

        var person = await factory.SignUpAsync();
        await AssertProblemAsync(await JoinAsync(person.Client, old), HttpStatusCode.BadRequest, JoinController.WrongCode);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(person.Client, code)).StatusCode);
    }

    [Fact]
    public async Task Only_user_managers_see_the_code_and_platform_admins_cannot_join()
    {
        var company = await factory.CreateCompanyAsync();
        var planner = await factory.AddMemberAsync(company, "Planner");
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/join-code")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.PostAsync("/api/join-code/renew", null)).StatusCode);

        await AssertProblemAsync(await JoinAsync(factory.PlatformAdmin(), await CodeAsync(company.Admin.Client)),
            HttpStatusCode.Conflict, JoinController.PlatformAdminsDontJoin);
    }

    [Fact]
    public async Task A_suspended_company_loses_access_at_once_and_gets_it_back()
    {
        var company = await factory.CreateCompanyAsync();
        var planner = await factory.AddMemberAsync(company, "Planner");
        var platform = factory.PlatformAdmin();
        Assert.Equal(HttpStatusCode.OK, (await planner.Client.GetAsync("/api/plants")).StatusCode);

        await AssertProblemAsync(
            await platform.PostAsJsonAsync($"/api/platform/tenants/{company.TenantId}/suspend", new { reason = " " }),
            HttpStatusCode.BadRequest, PlatformController.SuspendReasonNeeded);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PostAsJsonAsync(
            $"/api/platform/tenants/{company.TenantId}/suspend", new { reason = "Subscription unpaid" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/plants")).StatusCode);
        var me = await MeAsync(company.Admin.Client);
        Assert.True(me.CompanySuspended);
        Assert.Empty(me.Permissions);

        // Nobody new can join a paused company.
        var code = await CodeAsyncAs(company.TenantId);
        var person = await factory.SignUpAsync();
        await AssertProblemAsync(await JoinAsync(person.Client, code), HttpStatusCode.Conflict, JoinController.CompanyPaused);

        var tenants = await platform.GetFromJsonAsync<List<TenantSummaryDto>>("/api/platform/tenants", ApiFactory.Json);
        Assert.Equal(TenantStatus.Suspended, tenants!.Single(t => t.Id == company.TenantId).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await platform.PostAsJsonAsync(
            $"/api/platform/tenants/{company.TenantId}/reactivate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.Client.GetAsync("/api/plants")).StatusCode);
        Assert.False((await MeAsync(company.Admin.Client)).CompanySuspended);
        Assert.Contains(await TestData.AuditAsync(company.Admin.Client),
            e => e.Action == "tenant.suspended" && e.ChangesJson!.Contains("Subscription unpaid"));
    }

    [Fact]
    public async Task The_platform_list_counts_waiting_people_and_admins()
    {
        var company = await factory.CreateCompanyAsync();
        await factory.AddMemberAsync(company, approve: false);

        var tenants = await factory.PlatformAdmin().GetFromJsonAsync<List<TenantSummaryDto>>("/api/platform/tenants", ApiFactory.Json);
        var row = tenants!.Single(t => t.Id == company.TenantId);
        Assert.Equal(2, row.UserCount);
        Assert.Equal(1, row.PendingCount);
        Assert.Equal(1, row.AdminCount);
        Assert.NotNull(row.CreatedAtUtc);
    }

    // The code of a company whose admin can no longer call the API (it is suspended), read from the database.
    private async Task<string> CodeAsyncAs(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        return JoinCodes.Format(db.Tenants.Single(t => t.Id == tenantId).JoinCode!);
    }
}
