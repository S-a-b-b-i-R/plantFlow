using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Security;
using Testcontainers.MsSql;

namespace PlantFlow.Tests.Integration;

public record TestUser(Guid Id, string Email, HttpClient Client);

public record Company(Guid TenantId, TestUser Admin);

// One real SQL Server container and one running API for all integration tests.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string PlatformAdminEmail = "platform-admin@test.local";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Started once per test run. The Testcontainers reaper removes it when the run ends.
    private static readonly MsSqlContainer Sql = StartSql();

    private static MsSqlContainer StartSql()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not Development) so user secrets and the demo seeder are never used.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", Sql.GetConnectionString() + ";TrustServerCertificate=True");
        builder.UseSetting("Firebase:ProjectId", "plantflow-tests");
        builder.UseSetting("Bootstrap:PlatformAdminEmail", PlatformAdminEmail);
        builder.UseSetting("Database:MigrateOnStartup", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient ClientFor(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, $"uid-{email}|{email}");
        return client;
    }

    public HttpClient PlatformAdmin() => ClientFor(PlatformAdminEmail);

    // A brand-new Google user: the first call creates them as Pending in the Platform tenant.
    public async Task<TestUser> SignUpAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var client = ClientFor(email);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        return new TestUser(me!.Id, email, client);
    }

    // A new company with its first admin, done the same way a real platform admin would.
    public async Task<Company> CreateCompanyAsync()
    {
        var slug = "t" + Guid.NewGuid().ToString("N")[..12];
        var platform = PlatformAdmin();

        var created = await platform.PostAsJsonAsync("/api/platform/tenants", new { name = $"Company {slug}", slug });
        created.EnsureSuccessStatusCode();
        var tenant = (await created.Content.ReadFromJsonAsync<TenantSummaryDto>(Json))!;

        var admin = await SignUpAsync();
        var assign = await platform.PostAsJsonAsync(
            $"/api/platform/users/{admin.Id}/assign", new { tenantId = tenant.Id, asAdmin = true });
        assign.EnsureSuccessStatusCode();

        return new Company(tenant.Id, admin);
    }

    public async Task<TestUser> AddMemberAsync(Company company, string? roleName = null, bool approve = true)
    {
        var user = await SignUpAsync();

        (await PlatformAdmin().PostAsJsonAsync(
            $"/api/platform/users/{user.Id}/assign", new { tenantId = company.TenantId, asAdmin = false }))
            .EnsureSuccessStatusCode();

        if (approve)
        {
            (await company.Admin.Client.PostAsync($"/api/users/{user.Id}/approve", null)).EnsureSuccessStatusCode();
        }

        if (roleName is not null)
        {
            var roles = await company.Admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/roles", Json);
            var role = roles!.Single(r => r.Name == roleName);
            (await company.Admin.Client.PutAsJsonAsync($"/api/users/{user.Id}/roles", new { roleIds = new[] { role.Id } }))
                .EnsureSuccessStatusCode();
        }

        return user;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;