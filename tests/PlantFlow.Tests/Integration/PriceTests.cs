using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Pricing;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class PriceTests(ApiFactory factory)
{

    private static Task<HttpResponseMessage> PostPriceAsync(
        HttpClient client, Guid productId, decimal price, string from = "2026-01-01", string? to = "2026-12-31",
        Guid? plantId = null, Guid? customerId = null) =>
        client.PostAsJsonAsync("/api/prices",
            new { productId, plantId, customerId, pricePerCft = price, validFrom = from, validTo = to });

    private static async Task<PriceDto> CreatePriceAsync(
        HttpClient client, Guid productId, decimal price, string from = "2026-01-01", string? to = "2026-12-31",
        Guid? plantId = null, Guid? customerId = null)
    {
        var response = await PostPriceAsync(client, productId, price, from, to, plantId, customerId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PriceDto>(ApiFactory.Json))!;
    }

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, Guid id, string action) =>
        client.PostAsync($"/api/prices/{id}/{action}", null);

    private static async Task ActivateAsync(HttpClient client, Guid id)
    {
        (await ActAsync(client, id, "submit")).EnsureSuccessStatusCode();
        (await ActAsync(client, id, "approve")).EnsureSuccessStatusCode();
    }

    private static Task<PriceDto?> GetPriceAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<PriceDto>($"/api/prices/{id}", ApiFactory.Json);

    private static Task<HttpResponseMessage> LookupAsync(
        HttpClient client, Guid productId, Guid plantId, string date = "2026-06-01", Guid? customerId = null) =>
        client.GetAsync($"/api/prices/lookup?productId={productId}&plantId={plantId}&date={date}"
                        + (customerId is null ? "" : $"&customerId={customerId}"));

    private static async Task<PriceMatch> MatchAsync(
        HttpClient client, Guid productId, Guid plantId, string date = "2026-06-01", Guid? customerId = null)
    {
        var response = await LookupAsync(client, productId, plantId, date, customerId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PriceMatch>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task A_price_goes_from_draft_to_active_and_can_be_rejected_first()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);

        var draft = await CreatePriceAsync(admin, productId, 520m);
        Assert.Equal(PriceStatus.Draft, draft.Status);

        (await ActAsync(admin, draft.Id, "submit")).EnsureSuccessStatusCode();

        var noReason = await admin.PostAsJsonAsync($"/api/prices/{draft.Id}/reject", new { reason = "" });
        var reject = await admin.PostAsJsonAsync($"/api/prices/{draft.Id}/reject", new { reason = "Too low for this grade" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var rejected = await GetPriceAsync(admin, draft.Id);
        Assert.Equal(PriceStatus.Draft, rejected!.Status);
        Assert.Equal("Too low for this grade", rejected.RejectionReason);

        await ActivateAsync(admin, draft.Id);
        var active = await GetPriceAsync(admin, draft.Id);
        Assert.Equal(PriceStatus.Active, active!.Status);
        Assert.Null(active.RejectionReason);
        Assert.NotNull(active.ApprovedAtUtc);

        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync(admin, draft.Id, "expire")).StatusCode);
        Assert.Equal(PriceStatus.Expired, (await GetPriceAsync(admin, draft.Id))!.Status);
    }

    [Fact]
    public async Task Only_a_draft_can_be_edited()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var draft = await CreatePriceAsync(admin, productId, 520m);

        var edit = new { productId, pricePerCft = 530.5m, validFrom = "2026-02-01", validTo = "2026-11-30" };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/prices/{draft.Id}", edit)).StatusCode);

        var reloaded = await GetPriceAsync(admin, draft.Id);
        Assert.Equal(530.5m, reloaded!.PricePerCft);
        Assert.Equal(new DateOnly(2026, 2, 1), reloaded.ValidFrom);

        (await ActAsync(admin, draft.Id, "submit")).EnsureSuccessStatusCode();
        var whilePending = await admin.PutAsJsonAsync($"/api/prices/{draft.Id}", edit);
        (await ActAsync(admin, draft.Id, "approve")).EnsureSuccessStatusCode();
        var whileActive = await admin.PutAsJsonAsync($"/api/prices/{draft.Id}", edit);

        Assert.Equal(HttpStatusCode.Conflict, whilePending.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, whileActive.StatusCode);
    }

    [Fact]
    public async Task Overlapping_active_prices_in_the_same_scope_are_refused_at_approval()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var plantId = await CreatePlantAsync(admin, "P1");
        var customerId = await CreateCustomerAsync(admin, "C1");

        await CreateActivePriceAsync(admin, productId, 500m, "2026-01-01", "2026-06-30");

        // One day of overlap is refused. The refused price stays waiting for approval.
        var overlapping = await CreatePriceAsync(admin, productId, 510m, "2026-06-30", "2026-12-31");
        (await ActAsync(admin, overlapping.Id, "submit")).EnsureSuccessStatusCode();
        var refused = await ActAsync(admin, overlapping.Id, "approve");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(PriceStatus.PendingApproval, (await GetPriceAsync(admin, overlapping.Id))!.Status);

        // The day after the first one ends is fine.
        var adjacent = await CreateActivePriceAsync(admin, productId, 510m, "2026-07-01", "2026-12-31");

        // An open-ended price overlaps everything that starts later.
        var openEnded = await CreatePriceAsync(admin, productId, 520m, "2026-12-01", null);
        (await ActAsync(admin, openEnded.Id, "submit")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await ActAsync(admin, openEnded.Id, "approve")).StatusCode);

        // Other scopes (a plant, a customer) never clash with the general price.
        await CreateActivePriceAsync(admin, productId, 530m, "2026-01-01", "2026-12-31", plantId: plantId);
        await CreateActivePriceAsync(admin, productId, 540m, "2026-01-01", "2026-12-31", customerId: customerId);

        // Expiring the blocking price frees the period.
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync(admin, adjacent.Id, "expire")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync(admin, openEnded.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task The_lookup_picks_the_most_specific_active_price()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var p1 = await CreatePlantAsync(admin, "P1");
        var p2 = await CreatePlantAsync(admin, "P2");
        var customerId = await CreateCustomerAsync(admin, "C1");

        await CreateActivePriceAsync(admin, productId, 100m);
        await CreateActivePriceAsync(admin, productId, 110m, plantId: p1);
        await CreateActivePriceAsync(admin, productId, 120m, customerId: customerId);
        await CreateActivePriceAsync(admin, productId, 130m, plantId: p1, customerId: customerId);

        var customerAtP1 = await MatchAsync(admin, productId, p1, customerId: customerId);
        var customerAtP2 = await MatchAsync(admin, productId, p2, customerId: customerId);
        var anyoneAtP1 = await MatchAsync(admin, productId, p1);
        var anyoneAtP2 = await MatchAsync(admin, productId, p2);

        Assert.Equal((130m, PriceScope.CustomerAndPlant), (customerAtP1.PricePerCft, customerAtP1.Scope));
        Assert.Equal((120m, PriceScope.Customer), (customerAtP2.PricePerCft, customerAtP2.Scope));
        Assert.Equal((110m, PriceScope.Plant), (anyoneAtP1.PricePerCft, anyoneAtP1.Scope));
        Assert.Equal((100m, PriceScope.General), (anyoneAtP2.PricePerCft, anyoneAtP2.Scope));

        // Outside the validity period there is no price.
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(admin, productId, p1, "2027-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(admin, productId, p1, "2025-12-31")).StatusCode);
    }

    [Fact]
    public async Task The_lookup_ignores_drafts_pending_and_expired_prices()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var plantId = await CreatePlantAsync(admin, "P1");

        var draft = await CreatePriceAsync(admin, productId, 100m);
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(admin, productId, plantId)).StatusCode);

        (await ActAsync(admin, draft.Id, "submit")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(admin, productId, plantId)).StatusCode);

        (await ActAsync(admin, draft.Id, "approve")).EnsureSuccessStatusCode();
        Assert.Equal(100m, (await MatchAsync(admin, productId, plantId)).PricePerCft);

        (await ActAsync(admin, draft.Id, "expire")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(admin, productId, plantId)).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var productId = await CreateProductAsync(admin);
        var inactiveProduct = await CreateProductAsync(admin, "OLD", "Inactive");
        var foreignPlant = await CreatePlantAsync(other.Admin.Client, "PX");
        var foreignCustomer = await CreateCustomerAsync(other.Admin.Client, "CX");

        var endsBeforeStart = await PostPriceAsync(admin, productId, 500m, "2026-06-01", "2026-05-01");
        var zeroPrice = await PostPriceAsync(admin, productId, 0m);
        var tooPrecise = await PostPriceAsync(admin, productId, 500.123m);
        var missingDate = await admin.PostAsJsonAsync("/api/prices", new { productId, pricePerCft = 500m });
        var unknownProduct = await PostPriceAsync(admin, Guid.NewGuid(), 500m);
        var inactive = await PostPriceAsync(admin, inactiveProduct, 500m);
        var plantOfOther = await PostPriceAsync(admin, productId, 500m, plantId: foreignPlant);
        var customerOfOther = await PostPriceAsync(admin, productId, 500m, customerId: foreignCustomer);

        Assert.All(
            new[] { endsBeforeStart, zeroPrice, tooPrecise, missingDate, unknownProduct, inactive, plantOfOther, customerOfOther },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task Status_changes_only_follow_the_allowed_order()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var price = await CreatePriceAsync(admin, productId, 500m);

        var approveDraft = await ActAsync(admin, price.Id, "approve");
        var expireDraft = await ActAsync(admin, price.Id, "expire");
        var rejectDraft = await admin.PostAsJsonAsync($"/api/prices/{price.Id}/reject", new { reason = "Nope nope" });

        await ActivateAsync(admin, price.Id);
        var submitActive = await ActAsync(admin, price.Id, "submit");
        var approveActive = await ActAsync(admin, price.Id, "approve");
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync(admin, price.Id, "expire")).StatusCode);
        var expireAgain = await ActAsync(admin, price.Id, "expire");

        Assert.All(
            new[] { approveDraft, expireDraft, rejectDraft, submitActive, approveActive, expireAgain },
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
    }

    [Fact]
    public async Task The_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var alpha = await CreateProductAsync(admin, "ALPHA");
        var bravo = await CreateProductAsync(admin, "BRAVO");
        var charlie = await CreateProductAsync(admin, "CHARLIE");

        var a = await CreatePriceAsync(admin, alpha, 500m);
        await CreatePriceAsync(admin, bravo, 510m);
        await CreatePriceAsync(admin, charlie, 520m);
        await ActivateAsync(admin, a.Id);

        var firstPage = await admin.GetFromJsonAsync<PagedResult<PriceDto>>("/api/prices?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<PriceDto>>("/api/prices?pageSize=2&page=2", ApiFactory.Json);
        var active = await admin.GetFromJsonAsync<PagedResult<PriceDto>>("/api/prices?status=Active", ApiFactory.Json);
        var byProduct = await admin.GetFromJsonAsync<PagedResult<PriceDto>>($"/api/prices?productId={bravo}", ApiFactory.Json);
        var found = await admin.GetFromJsonAsync<PagedResult<PriceDto>>("/api/prices?search=charl", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("ALPHA", Assert.Single(active!.Items).ProductCode);
        Assert.Equal("BRAVO", Assert.Single(byProduct!.Items).ProductCode);
        Assert.Equal("CHARLIE", Assert.Single(found!.Items).ProductCode);
    }

    [Fact]
    public async Task Permissions_are_separate_and_companies_cannot_see_each_others_prices()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var productId = await CreateProductAsync(company.Admin.Client);
        var plantId = await CreatePlantAsync(company.Admin.Client, "P1");
        await CreateActivePriceAsync(company.Admin.Client, productId, 500m);

        var rep = await factory.AddMemberAsync(company, "Sales Representative"); // has prices.view only

        var listAsRep = await rep.Client.GetAsync("/api/prices");
        var lookupAsRep = await LookupAsync(rep.Client, productId, plantId);
        var createAsRep = await PostPriceAsync(rep.Client, productId, 480m, "2027-01-01", "2027-12-31");

        (await company.Admin.Client.PutAsJsonAsync($"/api/users/{rep.Id}/permissions",
            new { overrides = new[] { new { permissionKey = Permissions.PricesEdit, isGranted = true } } }))
            .EnsureSuccessStatusCode();

        var draft = await CreatePriceAsync(rep.Client, productId, 480m, "2027-01-01", "2027-12-31");
        var submit = await ActAsync(rep.Client, draft.Id, "submit");
        var approveAsRep = await ActAsync(rep.Client, draft.Id, "approve");
        var approveAsAdmin = await ActAsync(company.Admin.Client, draft.Id, "approve");

        var seenByOther = await other.Admin.Client.GetFromJsonAsync<PagedResult<PriceDto>>("/api/prices", ApiFactory.Json);
        var getByOther = await other.Admin.Client.GetAsync($"/api/prices/{draft.Id}");
        var approveByOther = await ActAsync(other.Admin.Client, draft.Id, "approve");
        var lookupByOther = await LookupAsync(other.Admin.Client, productId, plantId);

        Assert.Equal(HttpStatusCode.OK, listAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lookupAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approveAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, approveAsAdmin.StatusCode);
        Assert.Empty(seenByOther!.Items);
        Assert.Equal(HttpStatusCode.NotFound, getByOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approveByOther.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, lookupByOther.StatusCode);
    }
}