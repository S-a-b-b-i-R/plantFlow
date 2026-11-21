using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Data;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// One rep owns each customer. Others don't see it until it opens up (no confirmed order for N days, or
// released by a manager); then the first confirmed order wins it.
[Collection("api")]
public class CustomerOwnershipTests(ApiFactory factory)
{
    private sealed record Team(
        Company Company, HttpClient Admin, TestUser Rana, Guid RanaRep, TestUser Bilal, Guid BilalRep, TestUser Manager);

    private async Task<Team> TeamAsync()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var rana = await factory.AddMemberAsync(company, "Sales Representative");
        var bilal = await factory.AddMemberAsync(company, "Sales Representative");
        var manager = await factory.AddMemberAsync(company, "Sales Manager");
        return new Team(company, admin, rana, await RepAsync(admin, "Rana", rana.Id),
            bilal, await RepAsync(admin, "Bilal", bilal.Id), manager);
    }

    private static async Task<Guid> RepAsync(HttpClient admin, string name, Guid userId)
    {
        var response = await admin.PostAsJsonAsync("/api/sales-reps", new { name, userId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SalesRepDto>(ApiFactory.Json))!.Id;
    }

    private static async Task<CustomerDto> AddCustomerAsync(
        HttpClient client, string code, Guid? salesRepId = null, string? phone = null)
    {
        var response = await client.PostAsJsonAsync("/api/customers",
            new { code, name = $"Customer {code}", type = "Contractor", phone, salesRepId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;
    }

    private static async Task<List<string>> CodesAsync(HttpClient client, string query = "")
    {
        var page = await client.GetFromJsonAsync<PagedResult<CustomerDto>>($"/api/customers?pageSize=100{query}", ApiFactory.Json);
        return page!.Items.Select(c => c.Code).Order().ToList();
    }

    private static async Task<CustomerDto> CustomerAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<CustomerDto>($"/api/customers/{id}", ApiFactory.Json))!;

    // Moves the owner's clock (and optionally the rep's last look at open customers) into the past.
    private async Task BackdateAsync(Guid customerId, int days, Guid? seenRepId = null, int seenDaysAgo = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        var started = DateTime.UtcNow.AddDays(-days);
        await db.Customers.IgnoreQueryFilters().Where(c => c.Id == customerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnershipStartedUtc, started));
        if (seenRepId is not null)
        {
            var seen = DateTime.UtcNow.AddDays(-seenDaysAgo);
            await db.SalesReps.IgnoreQueryFilters().Where(r => r.Id == seenRepId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.OpenCustomersSeenUtc, seen));
        }
    }

    private async Task<List<string>> AuditActionsAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlantFlowDbContext>();
        return await db.AuditLogs.IgnoreQueryFilters().Where(a => a.EntityId == customerId.ToString())
            .OrderBy(a => a.OccurredAtUtc).Select(a => a.Action).ToListAsync();
    }

    private static async Task ConfirmAsync(HttpClient admin, Guid orderId)
    {
        (await admin.PostAsync($"/api/orders/{orderId}/submit", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/approvals/sales-manager/approve",
            new { comment = (string?)null })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/confirm", new { method = "Phone" })).EnsureSuccessStatusCode();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string message)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reps_see_only_their_own_and_open_customers()
    {
        var t = await TeamAsync();
        var ranas = await AddCustomerAsync(t.Rana.Client, "C1");
        await AddCustomerAsync(t.Manager.Client, "C2");
        var planner = await factory.AddMemberAsync(t.Company, "Planner");

        Assert.Equal(["C1", "C2"], await CodesAsync(t.Rana.Client));
        Assert.Equal(["C2"], await CodesAsync(t.Bilal.Client));
        Assert.Equal(["C1"], await CodesAsync(t.Rana.Client, "&ownership=Mine"));
        Assert.Equal(["C2"], await CodesAsync(t.Rana.Client, "&ownership=Open"));
        Assert.Equal(["C1", "C2"], await CodesAsync(t.Manager.Client));
        Assert.Equal(["C1", "C2"], await CodesAsync(planner.Client));

        Assert.Equal(HttpStatusCode.NotFound, (await t.Bilal.Client.GetAsync($"/api/customers/{ranas.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Bilal.Client.GetAsync($"/api/customers/{ranas.Id}/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Bilal.Client.GetAsync($"/api/customers/{ranas.Id}/usage")).StatusCode);

        // Nor can Bilal change it, its sites or their distances.
        var siteId = await CreateSiteAsync(t.Rana.Client, ranas.Id);
        var plantId = await CreatePlantAsync(t.Admin, "P1");
        var edit = await t.Bilal.Client.PutAsJsonAsync($"/api/customers/{ranas.Id}",
            new { code = "C1", name = "Mine now", type = "Contractor", salesRepId = t.RanaRep });
        var addSite = await t.Bilal.Client.PostAsJsonAsync($"/api/customers/{ranas.Id}/sites", new { name = "Tower B" });
        var editSite = await t.Bilal.Client.PutAsJsonAsync($"/api/sites/{siteId}", new { name = "Tower Z" });
        var distances = await t.Bilal.Client.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId, distanceKm = 4m } } });
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, addSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, distances.StatusCode);
    }

    [Fact]
    public async Task A_customer_opens_up_when_its_days_run_out_unless_the_setting_is_off()
    {
        var t = await TeamAsync();
        var customer = await AddCustomerAsync(t.Rana.Client, "C1");
        Assert.False(customer.IsOpen);
        Assert.True(customer.OpensAtUtc > DateTime.UtcNow.AddDays(119));

        await BackdateAsync(customer.Id, 121);
        var seen = await CustomerAsync(t.Bilal.Client, customer.Id);
        Assert.True(seen.IsOpen);
        Assert.Equal(["C1"], await CodesAsync(t.Bilal.Client, "&ownership=Open"));
        Assert.Empty(await CodesAsync(t.Rana.Client, "&ownership=Mine"));

        var settings = (await t.Admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        Assert.Equal(120, settings.CustomerOwnershipDays);
        (await t.Admin.PutAsJsonAsync("/api/settings", settings with { CustomerOwnershipDays = 0 })).EnsureSuccessStatusCode();

        Assert.Empty(await CodesAsync(t.Bilal.Client));
        Assert.Null((await CustomerAsync(t.Rana.Client, customer.Id)).OpensAtUtc);
    }

    [Fact]
    public async Task The_first_confirmed_order_wins_an_open_customer_and_the_loser_can_finish_theirs()
    {
        var t = await TeamAsync();
        var w = await CreateOrderWorldAsync(t.Admin); // C1 is created by the admin with no rep, so it is open.
        var lines = new[] { LineBody(w.ProductId, 50m) };

        var ranasOrder = await CreateDraftAsync(t.Rana.Client, w, lines);
        var bilalsOrder = await CreateDraftAsync(t.Bilal.Client, w, lines);
        Assert.Equal(t.RanaRep, ranasOrder.Order.SalesRepId);
        Assert.Equal(t.BilalRep, bilalsOrder.Order.SalesRepId);

        await ConfirmAsync(t.Admin, bilalsOrder.Order.Id);
        var won = await CustomerAsync(t.Admin, w.CustomerId);
        Assert.Equal(t.BilalRep, won.SalesRepId);
        Assert.False(won.IsOpen);
        Assert.Contains("customer.owner_won", await AuditActionsAsync(w.CustomerId));

        // Rana still sees the customer while her order is under way, and can edit and finish it,
        // but she cannot start another one.
        Assert.Equal(["C1"], await CodesAsync(t.Rana.Client));
        var edited = await t.Rana.Client.PutAsJsonAsync($"/api/orders/{ranasOrder.Order.Id}",
            OrderBody(w, [LineBody(w.ProductId, 60m)]));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var another = await t.Rana.Client.PostAsJsonAsync("/api/orders", OrderBody(w, lines));
        await AssertProblemAsync(another, HttpStatusCode.BadRequest, "Choose an active customer");

        await ConfirmAsync(t.Admin, ranasOrder.Order.Id);
        Assert.Equal(t.BilalRep, (await CustomerAsync(t.Admin, w.CustomerId)).SalesRepId);

        // Once her order is closed, the customer is out of her sight.
        (await t.Admin.PostAsJsonAsync($"/api/orders/{ranasOrder.Order.Id}/cancel", new { reason = "Customer went with Bilal" }))
            .EnsureSuccessStatusCode();
        Assert.Empty(await CodesAsync(t.Rana.Client));
    }

    [Fact]
    public async Task Each_confirmed_order_restarts_the_owners_clock()
    {
        var t = await TeamAsync();
        var w = await CreateOrderWorldAsync(t.Admin);
        var order = await CreateDraftAsync(t.Rana.Client, w, [LineBody(w.ProductId, 50m)]);
        await ConfirmAsync(t.Admin, order.Order.Id);

        await BackdateAsync(w.CustomerId, 100);
        var second = await CreateDraftAsync(t.Rana.Client, w, [LineBody(w.ProductId, 50m)]);
        await ConfirmAsync(t.Admin, second.Order.Id);

        var customer = await CustomerAsync(t.Admin, w.CustomerId);
        Assert.Equal(t.RanaRep, customer.SalesRepId);
        Assert.True(customer.OpensAtUtc > DateTime.UtcNow.AddDays(119));
    }

    [Fact]
    public async Task A_reps_orders_are_always_their_own()
    {
        var t = await TeamAsync();
        var w = await CreateOrderWorldAsync(t.Admin);
        var unlinked = await factory.AddMemberAsync(t.Company, "Sales Representative");

        var namesOther = await t.Rana.Client.PostAsJsonAsync("/api/orders",
            new
            {
                customerId = w.CustomerId, siteId = w.SiteId, plantId = w.PlantId, requestedDate = "2026-06-01",
                distanceKm = 5m, salesRepId = t.BilalRep, lines = new[] { LineBody(w.ProductId, 50m) }
            });
        var noLink = await unlinked.Client.PostAsJsonAsync("/api/orders", OrderBody(w, [LineBody(w.ProductId, 50m)]));

        await AssertProblemAsync(namesOther, HttpStatusCode.Forbidden, OrdersController.OwnRepOnly);
        await AssertProblemAsync(noLink, HttpStatusCode.Forbidden, OrdersController.NoRepLink);

        // A sales manager still picks any rep for an order.
        var managers = await t.Manager.Client.PostAsJsonAsync("/api/orders",
            new
            {
                customerId = w.CustomerId, siteId = w.SiteId, plantId = w.PlantId, requestedDate = "2026-06-01",
                distanceKm = 5m, salesRepId = t.BilalRep, lines = new[] { LineBody(w.ProductId, 50m) }
            });
        Assert.Equal(HttpStatusCode.Created, managers.StatusCode);
        var detail = await managers.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json);
        Assert.Equal(t.BilalRep, detail!.Order.SalesRepId);
    }

    [Fact]
    public async Task A_manager_reassigns_or_releases_a_customer_with_a_reason()
    {
        var t = await TeamAsync();
        var customer = await AddCustomerAsync(t.Rana.Client, "C1");
        var route = $"/api/customers/{customer.Id}/owner";

        var byRep = await t.Rana.Client.PutAsJsonAsync(route, new { salesRepId = t.BilalRep, reason = "Moving on" });
        var noReason = await t.Manager.Client.PutAsJsonAsync(route, new { salesRepId = t.BilalRep, reason = "" });
        var viaEdit = await t.Manager.Client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            new { code = "C1", name = "Customer C1", type = "Contractor", salesRepId = t.BilalRep });
        Assert.Equal(HttpStatusCode.Forbidden, byRep.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        await AssertProblemAsync(viaEdit, HttpStatusCode.Conflict, CustomersController.UseChangeOwner);

        var moved = await t.Manager.Client.PutAsJsonAsync(route, new { salesRepId = t.BilalRep, reason = "Rana left Dhaka" });
        var again = await t.Manager.Client.PutAsJsonAsync(route, new { salesRepId = t.BilalRep, reason = "Again" });
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        await AssertProblemAsync(again, HttpStatusCode.Conflict, CustomersController.AlreadyOwned);
        Assert.Equal(["C1"], await CodesAsync(t.Bilal.Client));
        Assert.Empty(await CodesAsync(t.Rana.Client));

        var released = await t.Manager.Client.PutAsJsonAsync(route, new { salesRepId = (Guid?)null, reason = "Nobody sells here" });
        var releasedAgain = await t.Manager.Client.PutAsJsonAsync(route, new { salesRepId = (Guid?)null, reason = "Again" });
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        await AssertProblemAsync(releasedAgain, HttpStatusCode.Conflict, CustomersController.AlreadyOpen);
        Assert.True((await CustomerAsync(t.Rana.Client, customer.Id)).IsOpen);

        // A lapsed customer can be given back to the same rep, which restarts the clock.
        var lapsed = await AddCustomerAsync(t.Rana.Client, "C2");
        await BackdateAsync(lapsed.Id, 130);
        var renewed = await t.Manager.Client.PutAsJsonAsync($"/api/customers/{lapsed.Id}/owner",
            new { salesRepId = t.RanaRep, reason = "Big project starting" });
        Assert.Equal(HttpStatusCode.NoContent, renewed.StatusCode);
        Assert.False((await CustomerAsync(t.Admin, lapsed.Id)).IsOpen);

        Assert.Equal(["customer.created", "customer.reassigned", "customer.released"], await AuditActionsAsync(customer.Id));
    }

    [Fact]
    public async Task Another_company_cannot_change_a_customers_owner()
    {
        var t = await TeamAsync();
        var customer = await AddCustomerAsync(t.Rana.Client, "C1");
        var other = await factory.CreateCompanyAsync();

        var response = await other.Admin.Client.PutAsJsonAsync($"/api/customers/{customer.Id}/owner",
            new { salesRepId = (Guid?)null, reason = "Not ours" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(t.RanaRep, (await CustomerAsync(t.Admin, customer.Id)).SalesRepId);
    }

    [Fact]
    public async Task Adding_an_existing_customer_says_who_owns_it()
    {
        var t = await TeamAsync();
        await AddCustomerAsync(t.Rana.Client, "C1", phone: "01711000000");
        await AddCustomerAsync(t.Manager.Client, "C2", phone: "01811000000");

        var sameCode = await t.Bilal.Client.PostAsJsonAsync("/api/customers",
            new { code = "c1", name = "Copy", type = "Contractor" });
        var samePhone = await t.Bilal.Client.PostAsJsonAsync("/api/customers",
            new { code = "C9", name = "Copy", type = "Contractor", phone = "01711000000" });
        var openOne = await t.Bilal.Client.PostAsJsonAsync("/api/customers",
            new { code = "C8", name = "Copy", type = "Contractor", phone = "01811000000" });

        await AssertProblemAsync(sameCode, HttpStatusCode.Conflict, "It belongs to Rana.");
        await AssertProblemAsync(samePhone, HttpStatusCode.Conflict, "Customer C1 (C1) already has this phone number. It belongs to Rana.");
        await AssertProblemAsync(openOne, HttpStatusCode.Conflict, "It is open, so you can find it under Open customers.");
    }

    [Fact]
    public async Task The_badge_counts_customers_that_opened_since_the_rep_last_looked()
    {
        var t = await TeamAsync();
        var lapsing = await AddCustomerAsync(t.Rana.Client, "C1");
        var released = await AddCustomerAsync(t.Rana.Client, "C2");
        var stillOwned = await AddCustomerAsync(t.Rana.Client, "C3");
        await (await t.Bilal.Client.PostAsync("/api/customers/opened/seen", null)).Content.ReadAsStringAsync();

        async Task<int> CountAsync(HttpClient client) =>
            (await client.GetFromJsonAsync<OpenedCustomersDto>("/api/customers/opened", ApiFactory.Json))!.Count;

        Assert.Equal(0, await CountAsync(t.Bilal.Client));

        (await t.Manager.Client.PutAsJsonAsync($"/api/customers/{released.Id}/owner",
            new { salesRepId = (Guid?)null, reason = "Free for all" })).EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAsync(t.Bilal.Client));

        // C1's days ran out 5 days ago, after Bilal last looked 10 days ago. C3 is still Rana's.
        await BackdateAsync(lapsing.Id, 125, t.BilalRep, seenDaysAgo: 10);
        await BackdateAsync(stillOwned.Id, 30);
        Assert.Equal(2, await CountAsync(t.Bilal.Client));

        var seen = await t.Bilal.Client.PostAsync("/api/customers/opened/seen", null);
        Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
        Assert.Equal(0, await CountAsync(t.Bilal.Client));

        // Managers see every customer anyway, and people without customers.manage have no badge.
        Assert.Equal(0, await CountAsync(t.Manager.Client));
        var planner = await factory.AddMemberAsync(t.Company, "Planner");
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync("/api/customers/opened")).StatusCode);
    }
}
