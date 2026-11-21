using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Auditing;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

// The audit log screen: filters, paging, who did it, and the CSV export.
[Collection("api")]
public class AuditTests(ApiFactory factory)
{
    // Writes entries straight to the log so each test controls the times, people and record types.
    private async Task SeedAsync(Guid tenantId, int utcOffsetMinutes, params AuditLog[] entries)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        await db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UtcOffsetMinutes, utcOffsetMinutes));
        foreach (var entry in entries) entry.TenantId = tenantId;
        db.AuditLogs.AddRange(entries);
        await db.SaveChangesAsync();
    }

    private static AuditLog Entry(string action, string type, string id, DateTime utc, Guid? userId, string? changes = null) =>
        new() { Action = action, EntityType = type, EntityId = id, OccurredAtUtc = utc, UserId = userId, ChangesJson = changes };

    private static async Task<PagedResult<AuditLogDto>> PageAsync(HttpClient c, string query) =>
        (await c.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/audit?{query}", ApiFactory.Json))!;

    [Fact]
    public async Task The_log_filters_by_company_day_person_record_and_text_and_names_who_did_it()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var planner = await factory.AddMemberAsync(company, "Planner");
        // UTC+6: 20:00 UTC on 1 March is 02:00 on 2 March in the company.
        await SeedAsync(company.TenantId, 360,
            Entry("order.confirmed", "Order", "ORD-1", new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc), planner.Id,
                """{"Note":"rush pour"}"""),
            Entry("plant.updated", "Plant", "PL-1", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), company.Admin.Id),
            Entry("nightly.cleanup", "SystemCheck", "T-1", new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc), null));

        var day = await PageAsync(admin, "from=2026-03-02&to=2026-03-02");
        var order = Assert.Single(day.Items);
        Assert.Equal("ORD-1", order.EntityId);
        Assert.Equal(planner.Email, order.UserName);

        Assert.Equal("PL-1", Assert.Single((await PageAsync(admin, "from=2026-03-01&to=2026-03-01")).Items).EntityId);
        Assert.Equal("ORD-1", Assert.Single((await PageAsync(admin, $"userId={planner.Id}&entityType=Order")).Items).EntityId);
        Assert.Equal("ORD-1", Assert.Single((await PageAsync(admin, "search=rush")).Items).EntityId);
        Assert.Equal("PL-1", Assert.Single((await PageAsync(admin, "entityType=Plant&entityId=PL-1")).Items).EntityId);
        Assert.Null(Assert.Single((await PageAsync(admin, "entityType=SystemCheck")).Items).UserName);

        var filters = (await admin.GetFromJsonAsync<AuditFiltersDto>("/api/audit/filters", ApiFactory.Json))!;
        Assert.Contains("Order", filters.EntityTypes);
        Assert.Contains(filters.People, p => p.Id == planner.Id);
    }

    [Fact]
    public async Task The_log_pages_newest_first()
    {
        var company = await factory.CreateCompanyAsync();
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedAsync(company.TenantId, 0, Enumerable.Range(0, 5)
            .Select(i => Entry("plant.updated", "PagingCheck", $"P{i}", start.AddMinutes(i), null)).ToArray());

        var first = await PageAsync(company.Admin.Client, "entityType=PagingCheck&pageSize=2");
        var last = await PageAsync(company.Admin.Client, "entityType=PagingCheck&pageSize=2&page=3");
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(["P4", "P3"], first.Items.Select(i => i.EntityId));
        Assert.Equal("P0", Assert.Single(last.Items).EntityId);
    }

    [Fact]
    public async Task The_export_is_csv_in_company_time_safe_for_spreadsheets_and_is_itself_recorded()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        await SeedAsync(company.TenantId, 360,
            Entry("customer.updated", "Customer", "C-1", new DateTime(2026, 4, 1, 20, 30, 0, DateTimeKind.Utc), company.Admin.Id,
                """{"Name":"=HYPERLINK(\"x\")"}"""));

        var response = await admin.GetAsync("/api/audit/export?entityType=Customer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("When (company time),Who", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("\"2026-04-02 02:30:00\",", lines[1]);
        Assert.Contains("\"customer.updated\",\"Customer\",\"C-1\"", lines[1]);

        // A value starting with "=" stays text in a spreadsheet.
        var formula = await admin.GetAsync("/api/audit/export?search=HYPERLINK");
        var formulaCsv = await formula.Content.ReadAsStringAsync();
        Assert.Contains("\"{\"\"Name\"\":", formulaCsv);

        Assert.Contains(await TestData.AuditAsync(admin), e => e.Action == "audit.exported");
    }

    [Fact]
    public async Task A_formula_at_the_start_of_a_cell_is_neutralised()
    {
        var company = await factory.CreateCompanyAsync();
        await SeedAsync(company.TenantId, 0,
            Entry("=cmd()", "FormulaCheck", "-1", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), null));

        var csv = await (await company.Admin.Client.GetAsync("/api/audit/export?entityType=FormulaCheck")).Content.ReadAsStringAsync();
        Assert.Contains("\"'=cmd()\",\"FormulaCheck\",\"'-1\"", csv);
    }

    [Fact]
    public async Task An_export_over_the_limit_is_refused()
    {
        var company = await factory.CreateCompanyAsync();
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedAsync(company.TenantId, 0, Enumerable.Range(0, AuditController.ExportLimit + 1)
            .Select(i => Entry("bulk", "BulkCheck", i.ToString(), start.AddSeconds(i), null)).ToArray());

        var response = await company.Admin.Client.GetAsync("/api/audit/export?entityType=BulkCheck");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(AuditController.ExportTooLarge, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_people_with_audit_view_see_the_log()
    {
        var company = await factory.CreateCompanyAsync();
        var planner = await factory.AddMemberAsync(company, "Planner");

        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/audit/filters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/audit/export")).StatusCode);
    }

    [Fact]
    public async Task Another_company_s_entries_never_show_or_export()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        await SeedAsync(other.TenantId, 0,
            Entry("plant.updated", "IsolationCheck", "X-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), other.Admin.Id));

        Assert.Empty((await PageAsync(company.Admin.Client, "entityType=IsolationCheck")).Items);
        var filters = (await company.Admin.Client.GetFromJsonAsync<AuditFiltersDto>("/api/audit/filters", ApiFactory.Json))!;
        Assert.DoesNotContain("IsolationCheck", filters.EntityTypes);
        Assert.DoesNotContain(filters.People, p => p.Id == other.Admin.Id);
        var csv = await (await company.Admin.Client.GetAsync("/api/audit/export?entityType=IsolationCheck")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("X-1", csv);
    }
}
