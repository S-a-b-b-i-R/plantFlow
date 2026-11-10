using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class TenantIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task A_company_admin_only_sees_their_own_users()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var memberOfA = await factory.AddMemberAsync(a);

        var seenByB = await b.Admin.Client.GetFromJsonAsync<List<UserDto>>("/api/users", ApiFactory.Json);

        Assert.Single(seenByB!);
        Assert.Equal(b.Admin.Id, seenByB![0].Id);
        Assert.DoesNotContain(seenByB, u => u.Id == memberOfA.Id);
    }

    [Fact]
    public async Task Another_companys_user_cannot_be_read_or_changed()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var memberOfA = await factory.AddMemberAsync(a, approve: false);

        var get = await b.Admin.Client.GetAsync($"/api/users/{memberOfA.Id}");
        var approve = await b.Admin.Client.PostAsync($"/api/users/{memberOfA.Id}/approve", null);
        var disable = await b.Admin.Client.PostAsync($"/api/users/{memberOfA.Id}/disable", null);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);

        // ...and A's user is still Pending, untouched.
        var stillPending = await a.Admin.Client.GetFromJsonAsync<UserDetailDto>(
            $"/api/users/{memberOfA.Id}", ApiFactory.Json);
        Assert.Equal(PlantFlow.Api.Domain.Identity.UserStatus.Pending, stillPending!.Status);
    }

    [Fact]
    public async Task Roles_are_per_company_and_cannot_be_borrowed()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var memberOfB = await factory.AddMemberAsync(b);

        var rolesOfA = await a.Admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles", ApiFactory.Json);
        var rolesOfB = await b.Admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles", ApiFactory.Json);

        Assert.Empty(rolesOfA!.Select(r => r.Id).Intersect(rolesOfB!.Select(r => r.Id)));

        var response = await b.Admin.Client.PutAsJsonAsync(
            $"/api/users/{memberOfB.Id}/roles", new { roleIds = new[] { rolesOfA![0].Id } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_audit_log_of_one_company_never_shows_another_companys_events()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var memberOfA = await factory.AddMemberAsync(a);

        var auditOfA = await a.Admin.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit", ApiFactory.Json);
        var auditOfB = await b.Admin.Client.GetFromJsonAsync<List<AuditLogDto>>("/api/audit", ApiFactory.Json);

        Assert.Contains(auditOfA!, e => e.EntityId == memberOfA.Id.ToString());
        Assert.DoesNotContain(auditOfB!, e => e.EntityId == memberOfA.Id.ToString());
    }
}