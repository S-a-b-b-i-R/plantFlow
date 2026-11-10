using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class CustomerAndSiteTests(ApiFactory factory)
{
    private static async Task<SalesRepDto> CreateRepAsync(
        HttpClient client, string name, Guid? managerId = null, string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/sales-reps", new { name, region = "Dhaka", managerId, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SalesRepDto>(ApiFactory.Json))!;
    }

    private static async Task<CustomerDto> CreateCustomerAsync(
        HttpClient client, string code, string name = "Test Customer", Guid? salesRepId = null)
    {
        var response = await client.PostAsJsonAsync("/api/customers",
            new { code, name, type = "Contractor", phone = "01700000000", salesRepId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;
    }

    private static Task<HttpResponseMessage> AddSiteAsync(
        HttpClient client, Guid customerId, string name, decimal? lat = null, decimal? lng = null) =>
        client.PostAsJsonAsync($"/api/customers/{customerId}/sites",
            new { name, address = "Plot 1, Road 2", latitude = lat, longitude = lng });

    // ---- Sales reps ----

    [Fact]
    public async Task Sales_rep_hierarchy_cannot_contain_loops()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var boss = await CreateRepAsync(admin, "Boss");
        var rep = await CreateRepAsync(admin, "Rep", managerId: boss.Id);

        var loop = await admin.PutAsJsonAsync($"/api/sales-reps/{boss.Id}", new { name = "Boss", managerId = rep.Id });
        var self = await admin.PutAsJsonAsync($"/api/sales-reps/{boss.Id}", new { name = "Boss", managerId = boss.Id });

        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task A_user_links_to_one_sales_rep_and_only_from_the_same_company()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var member = await factory.AddMemberAsync(company, "Sales Representative");

        var first = await admin.PostAsJsonAsync("/api/sales-reps", new { name = "Linked", userId = member.Id });
        var second = await admin.PostAsJsonAsync("/api/sales-reps", new { name = "Duplicate", userId = member.Id });

        var otherCompany = await factory.CreateCompanyAsync();
        var foreignUser = await factory.AddMemberAsync(otherCompany);
        var foreign = await admin.PostAsJsonAsync("/api/sales-reps", new { name = "Foreign", userId = foreignUser.Id });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    // ---- Customers ----

    [Fact]
    public async Task Customer_codes_are_unique_per_company_and_inactive_reps_cannot_be_assigned()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var activeRep = await CreateRepAsync(a.Admin.Client, "Active Rep");
        var retiredRep = await CreateRepAsync(a.Admin.Client, "Retired Rep", status: "Inactive");

        var customer = await CreateCustomerAsync(a.Admin.Client, "acme", salesRepId: activeRep.Id);
        Assert.Equal("ACME", customer.Code);
        Assert.Equal("Active Rep", customer.SalesRepName);

        var duplicate = await a.Admin.Client.PostAsJsonAsync("/api/customers", new { code = "ACME", name = "Other" });
        var sameCodeElsewhere = await b.Admin.Client.PostAsJsonAsync("/api/customers", new { code = "ACME", name = "Other" });
        var withRetiredRep = await a.Admin.Client.PostAsJsonAsync("/api/customers",
            new { code = "NEW1", name = "New", salesRepId = retiredRep.Id });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sameCodeElsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withRetiredRep.StatusCode);
    }

    [Fact]
    public async Task Customer_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var rep = await CreateRepAsync(admin, "Filter Rep");
        await CreateCustomerAsync(admin, "C1", "Alpha Builders", rep.Id);
        await CreateCustomerAsync(admin, "C2", "Bravo Developers");
        await CreateCustomerAsync(admin, "C3", "Charlie Contractors");

        var firstPage = await admin.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers?pageSize=2&page=2", ApiFactory.Json);
        var found = await admin.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers?search=bravo", ApiFactory.Json);
        var byRep = await admin.GetFromJsonAsync<PagedResult<CustomerDto>>($"/api/customers?salesRepId={rep.Id}", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("C2", Assert.Single(found!.Items).Code);
        Assert.Equal("C1", Assert.Single(byRep!.Items).Code);
    }

    // ---- Sites ----

    [Fact]
    public async Task Sites_belong_to_a_customer_with_unique_names_and_paired_coordinates()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var customer = await CreateCustomerAsync(admin, "SITES1");

        var created = await AddSiteAsync(admin, customer.Id, "Tower A", 23.8103m, 90.4125m);
        var duplicate = await AddSiteAsync(admin, customer.Id, "Tower A");
        var onlyLatitude = await AddSiteAsync(admin, customer.Id, "Tower B", lat: 23.8m);
        var outOfRange = await AddSiteAsync(admin, customer.Id, "Tower C", 123m, 90m);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, onlyLatitude.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);

        var sites = await admin.GetFromJsonAsync<PagedResult<SiteDto>>($"/api/customers/{customer.Id}/sites", ApiFactory.Json);
        Assert.Equal("Tower A", Assert.Single(sites!.Items).Name);

        var refreshed = await admin.GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}", ApiFactory.Json);
        Assert.Equal(1, refreshed!.SiteCount);
    }

    [Fact]
    public async Task The_same_site_name_is_fine_for_different_customers()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var first = await CreateCustomerAsync(admin, "S1");
        var second = await CreateCustomerAsync(admin, "S2");

        Assert.Equal(HttpStatusCode.Created, (await AddSiteAsync(admin, first.Id, "Main Site")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddSiteAsync(admin, second.Id, "Main Site")).StatusCode);
    }

    // ---- Permissions and tenant isolation ----

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var customer = await CreateCustomerAsync(company.Admin.Client, "VIEW1");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var list = await rep.Client.GetAsync("/api/customers");
        var sites = await rep.Client.GetAsync($"/api/customers/{customer.Id}/sites");
        var createCustomer = await rep.Client.PostAsJsonAsync("/api/customers", new { code = "X1", name = "Nope" });
        var createSite = await AddSiteAsync(rep.Client, customer.Id, "Nope");
        var createRep = await rep.Client.PostAsJsonAsync("/api/sales-reps", new { name = "Nope" });

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sites.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createSite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createRep.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_touch_customers_sites_or_reps()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var repOfA = await CreateRepAsync(a.Admin.Client, "A Rep");
        var customerOfA = await CreateCustomerAsync(a.Admin.Client, "SECRET");
        var siteResponse = await AddSiteAsync(a.Admin.Client, customerOfA.Id, "Secret Site");
        var siteOfA = (await siteResponse.Content.ReadFromJsonAsync<SiteDto>(ApiFactory.Json))!;

        var customersSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers", ApiFactory.Json);
        var repsSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<SalesRepDto>>("/api/sales-reps", ApiFactory.Json);
        var getCustomer = await b.Admin.Client.GetAsync($"/api/customers/{customerOfA.Id}");
        var getSite = await b.Admin.Client.GetAsync($"/api/sites/{siteOfA.Id}");
        var sitesOfA = await b.Admin.Client.GetAsync($"/api/customers/{customerOfA.Id}/sites");
        var addSite = await AddSiteAsync(b.Admin.Client, customerOfA.Id, "Injected");
        var updateCustomer = await b.Admin.Client.PutAsJsonAsync($"/api/customers/{customerOfA.Id}", new { code = "HACK", name = "Hacked" });
        var borrowRep = await b.Admin.Client.PostAsJsonAsync("/api/customers", new { code = "B1", name = "B", salesRepId = repOfA.Id });

        Assert.Empty(customersSeenByB!.Items);
        Assert.Empty(repsSeenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, getCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sitesOfA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, addSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, borrowRep.StatusCode);
    }
}