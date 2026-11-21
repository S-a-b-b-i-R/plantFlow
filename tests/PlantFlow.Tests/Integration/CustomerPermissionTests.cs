using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// Who may add and change customers, sites and sales reps, who picks the rep, and who sets VAT exemption.
[Collection("api")]
public class CustomerPermissionTests(ApiFactory factory)
{
    private static object CustomerBody(string code, Guid? salesRepId = null, bool vatExempt = false) =>
        new { code, name = $"Customer {code}", type = "Contractor", salesRepId, vatExempt };

    private static async Task<SalesRepDto> CreateRepAsync(HttpClient client, string name, Guid? userId = null)
    {
        var response = await client.PostAsJsonAsync("/api/sales-reps", new { name, userId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SalesRepDto>(ApiFactory.Json))!;
    }

    private static async Task<CustomerDto> ReadCustomerAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_linked_sales_rep_adds_customers_as_their_own()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var user = await factory.AddMemberAsync(company, "Sales Representative");
        var mine = await CreateRepAsync(admin, "Rana", user.Id);
        var other = await CreateRepAsync(admin, "Other");

        // Naming another rep is ignored: a rep's new customer is always their own.
        var customer = await ReadCustomerAsync(
            await user.Client.PostAsJsonAsync("/api/customers", CustomerBody("C1", other.Id)));
        var site = await user.Client.PostAsJsonAsync($"/api/customers/{customer.Id}/sites", new { name = "Tower A" });

        Assert.Equal(mine.Id, customer.SalesRepId);
        Assert.Equal(HttpStatusCode.Created, site.StatusCode);
    }

    [Fact]
    public async Task A_sales_rep_without_a_rep_link_cannot_add_customers()
    {
        var company = await factory.CreateCompanyAsync();
        var user = await factory.AddMemberAsync(company, "Sales Representative");

        var response = await user.Client.PostAsJsonAsync("/api/customers", CustomerBody("C1"));

        await AssertRefusedAsync(response, CustomersController.NoRepLink);
    }

    [Fact]
    public async Task Only_a_sales_manager_picks_or_changes_the_rep()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var repUser = await factory.AddMemberAsync(company, "Sales Representative");
        var mine = await CreateRepAsync(admin, "Rana", repUser.Id);
        var other = await CreateRepAsync(admin, "Other");
        var manager = await factory.AddMemberAsync(company, "Sales Manager");

        var picked = await ReadCustomerAsync(
            await manager.Client.PostAsJsonAsync("/api/customers", CustomerBody("C1", other.Id)));
        var unowned = await ReadCustomerAsync(
            await manager.Client.PostAsJsonAsync("/api/customers", CustomerBody("C2")));
        var own = await ReadCustomerAsync(
            await repUser.Client.PostAsJsonAsync("/api/customers", CustomerBody("C3")));

        var repChanges = await repUser.Client.PutAsJsonAsync($"/api/customers/{own.Id}", CustomerBody("C3", other.Id));
        var repEdits = await repUser.Client.PutAsJsonAsync($"/api/customers/{own.Id}",
            new { code = "C3", name = "Renamed", type = "Developer", salesRepId = mine.Id });
        var managerChanges = await manager.Client.PutAsJsonAsync($"/api/customers/{own.Id}", CustomerBody("C3", other.Id));

        Assert.Equal(other.Id, picked.SalesRepId);
        Assert.Null(unowned.SalesRepId);
        await AssertRefusedAsync(repChanges, CustomersController.RepChangeRefused);
        Assert.Equal(HttpStatusCode.NoContent, repEdits.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, managerChanges.StatusCode);
    }

    [Fact]
    public async Task Only_accounts_and_admin_set_vat_exemption()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var manager = await factory.AddMemberAsync(company, "Sales Manager");
        var accounts = await factory.AddMemberAsync(company, "Accounts");
        var customer = await ReadCustomerAsync(await admin.PostAsJsonAsync("/api/customers", CustomerBody("C1")));

        var createExempt = await manager.Client.PostAsJsonAsync("/api/customers", CustomerBody("C2", vatExempt: true));
        var editExempt = await manager.Client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            CustomerBody("C1", vatExempt: true));
        var managerEndpoint = await manager.Client.PutAsJsonAsync($"/api/customers/{customer.Id}/vat-exempt",
            new { vatExempt = true });
        var accountsSets = await accounts.Client.PutAsJsonAsync($"/api/customers/{customer.Id}/vat-exempt",
            new { vatExempt = true });
        var accountsEdits = await accounts.Client.PutAsJsonAsync($"/api/customers/{customer.Id}", CustomerBody("C1"));

        await AssertRefusedAsync(createExempt, CustomersController.VatRefused);
        await AssertRefusedAsync(editExempt, CustomersController.VatRefused);
        Assert.Equal(HttpStatusCode.Forbidden, managerEndpoint.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, accountsSets.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, accountsEdits.StatusCode);

        var after = await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}", ApiFactory.Json);
        Assert.True(after!.VatExempt);

        // A sales manager can still edit the exempt customer, as long as the flag is left alone.
        var managerEdit = await manager.Client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            new { code = "C1", name = "Renamed", type = "Contractor", vatExempt = true });
        Assert.Equal(HttpStatusCode.NoContent, managerEdit.StatusCode);
    }

    [Fact]
    public async Task Only_a_sales_manager_manages_sales_reps_and_sees_who_can_be_linked()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var repUser = await factory.AddMemberAsync(company, "Sales Representative");
        var manager = await factory.AddMemberAsync(company, "Sales Manager");

        var byRep = await repUser.Client.PostAsJsonAsync("/api/sales-reps", new { name = "Self made" });
        var linkableByRep = await repUser.Client.GetAsync("/api/sales-reps/linkable-users");
        var rep = await CreateRepAsync(manager.Client, "Rana", repUser.Id);
        var linkable = await manager.Client.GetFromJsonAsync<List<LinkableUserDto>>(
            "/api/sales-reps/linkable-users", ApiFactory.Json);
        var list = await admin.GetFromJsonAsync<PagedResult<SalesRepDto>>("/api/sales-reps", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, byRep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, linkableByRep.StatusCode);
        Assert.Equal(rep.Id, linkable!.Single(u => u.Id == repUser.Id).LinkedRepId);
        Assert.Null(linkable!.Single(u => u.Id == manager.Id).LinkedRepId);
        Assert.Equal(repUser.Email, list!.Items.Single(r => r.Id == rep.Id).UserEmail);
    }

    [Fact]
    public async Task Mine_returns_the_signed_in_users_rep_or_nothing()
    {
        var company = await factory.CreateCompanyAsync();
        var repUser = await factory.AddMemberAsync(company, "Sales Representative");
        var unlinked = await factory.AddMemberAsync(company, "Sales Representative");
        var rep = await CreateRepAsync(company.Admin.Client, "Rana", repUser.Id);

        var mine = await repUser.Client.GetFromJsonAsync<SalesRepDto>("/api/sales-reps/mine", ApiFactory.Json);
        var none = await unlinked.Client.GetAsync("/api/sales-reps/mine");

        Assert.Equal(rep.Id, mine!.Id);
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
    }

    [Fact]
    public async Task Usage_counts_open_orders_of_a_customer_and_a_site()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 50m)]);
        var cancelled = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 20m)]);
        (await admin.PostAsJsonAsync($"/api/orders/{cancelled.Order.Id}/cancel", new { reason = "Customer changed plans" }))
            .EnsureSuccessStatusCode();
        var otherSite = await CreateSiteAsync(admin, w.CustomerId, "Tower B");

        var customer = await admin.GetFromJsonAsync<OpenOrdersDto>($"/api/customers/{w.CustomerId}/usage", ApiFactory.Json);
        var site = await admin.GetFromJsonAsync<OpenOrdersDto>($"/api/sites/{w.SiteId}/usage", ApiFactory.Json);
        var emptySite = await admin.GetFromJsonAsync<OpenOrdersDto>($"/api/sites/{otherSite}/usage", ApiFactory.Json);

        Assert.Equal(1, customer!.OpenOrders);
        Assert.Equal(1, site!.OpenOrders);
        Assert.Equal(0, emptySite!.OpenOrders);
    }

    [Fact]
    public async Task Usage_needs_master_data_access_and_stays_inside_the_company()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var customerId = await CreateCustomerAsync(a.Admin.Client, "C1");
        var siteId = await CreateSiteAsync(a.Admin.Client, customerId);
        var dispatcher = (await factory.AddMemberAsync(a, "Dispatcher")).Client;

        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.GetAsync($"/api/customers/{customerId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.GetAsync($"/api/sites/{siteId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.Client.GetAsync($"/api/customers/{customerId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.Client.GetAsync($"/api/sites/{siteId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.Admin.Client.PutAsJsonAsync($"/api/customers/{customerId}/vat-exempt", new { vatExempt = true })).StatusCode);
    }
}
