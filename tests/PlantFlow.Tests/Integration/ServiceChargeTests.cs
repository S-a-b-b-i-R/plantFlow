using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class ServiceChargeTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> PostChargeAsync(
        HttpClient client, string code, string kind, string basis = "PerCft", decimal amount = 10m,
        string name = "Test Charge", string status = "Active") =>
        client.PostAsJsonAsync("/api/service-charges",
            new { code, name, kind, basis, defaultAmount = amount, status });

    private static async Task<ServiceChargeDto> CreateChargeAsync(
        HttpClient client, string code, string kind, string basis = "PerCft", decimal amount = 10m)
    {
        var response = await PostChargeAsync(client, code, kind, basis, amount);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServiceChargeDto>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task A_charge_is_created_with_a_normalized_code_and_its_basis()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var charge = await CreateChargeAsync(admin, "pump", "Pump", "PerCft", 12.50m);

        Assert.Equal("PUMP", charge.Code);
        Assert.Equal(ServiceChargeKind.Pump, charge.Kind);
        Assert.Equal(ChargeBasis.PerCft, charge.Basis);
        Assert.Equal(12.50m, charge.DefaultAmount);
    }

    [Fact]
    public async Task A_company_has_one_charge_per_kind_and_unique_codes()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();

        await CreateChargeAsync(a.Admin.Client, "PUMP", "Pump");

        var secondPump = await PostChargeAsync(a.Admin.Client, "PUMP-2", "Pump");
        var duplicateCode = await PostChargeAsync(a.Admin.Client, "pump", "NightDelivery");
        var otherCompany = await PostChargeAsync(b.Admin.Client, "PUMP", "Pump");

        Assert.Equal(HttpStatusCode.Conflict, secondPump.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCode.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCompany.StatusCode);
    }

    [Fact]
    public async Task A_charge_cannot_be_changed_to_a_kind_that_is_already_taken()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        await CreateChargeAsync(admin, "PUMP", "Pump");
        var night = await CreateChargeAsync(admin, "NIGHT", "NightDelivery", "Fixed", 500m);

        var takeKind = await admin.PutAsJsonAsync($"/api/service-charges/{night.Id}",
            new { code = "NIGHT", name = "Night", kind = "Pump", basis = "Fixed", defaultAmount = 500 });
        var keepOwnKind = await admin.PutAsJsonAsync($"/api/service-charges/{night.Id}",
            new { code = "NIGHT", name = "Night delivery fee", kind = "NightDelivery", basis = "Fixed", defaultAmount = 750 });

        Assert.Equal(HttpStatusCode.Conflict, takeKind.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, keepOwnKind.StatusCode);

        var reloaded = await admin.GetFromJsonAsync<ServiceChargeDto>($"/api/service-charges/{night.Id}", ApiFactory.Json);
        Assert.Equal(750m, reloaded!.DefaultAmount);
    }

    [Fact]
    public async Task Charge_input_is_validated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var negative = await PostChargeAsync(admin, "N1", "Pump", amount: -1m);
        var threeDecimals = await PostChargeAsync(admin, "N2", "ShortLoad", amount: 10.123m);
        var badKind = await PostChargeAsync(admin, "N3", "Parking");
        var badBasis = await PostChargeAsync(admin, "N4", "Pump", basis: "PerMile");
        var missingKind = await admin.PostAsJsonAsync("/api/service-charges",
            new { code = "N5", name = "No kind", basis = "Fixed", defaultAmount = 1 });
        var badCode = await PostChargeAsync(admin, "has space", "ExtraDistance");

        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, threeDecimals.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badBasis.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
    }

    [Fact]
    public async Task The_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        await CreateChargeAsync(admin, "PUMP", "Pump", "PerCft", 15m);
        await CreateChargeAsync(admin, "NIGHT", "NightDelivery", "Fixed", 500m);
        await CreateChargeAsync(admin, "SHORT", "ShortLoad", "PerLoad", 200m);
        var distance = await CreateChargeAsync(admin, "DIST", "ExtraDistance", "PerCft", 5m);

        (await admin.PutAsJsonAsync($"/api/service-charges/{distance.Id}", new
        {
            code = "DIST",
            name = "Extra distance",
            kind = "ExtraDistance",
            basis = "PerCft",
            defaultAmount = 5,
            status = "Inactive"
        })).EnsureSuccessStatusCode();

        var firstPage = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?pageSize=3", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?pageSize=3&page=2", ApiFactory.Json);
        var perCft = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?basis=PerCft", ApiFactory.Json);
        var pumps = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?kind=Pump", ApiFactory.Json);
        var inactive = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?status=Inactive", ApiFactory.Json);
        var byAmount = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?sort=-amount", ApiFactory.Json);

        Assert.Equal(4, firstPage!.TotalCount);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal(2, perCft!.TotalCount);
        Assert.Equal("PUMP", Assert.Single(pumps!.Items).Code);
        Assert.Equal("DIST", Assert.Single(inactive!.Items).Code);
        Assert.Equal("NIGHT", byAmount!.Items[0].Code);
    }

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var list = await rep.Client.GetAsync("/api/service-charges");
        var create = await PostChargeAsync(rep.Client, "NOPE", "Pump");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_charges()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var charge = await CreateChargeAsync(a.Admin.Client, "PRIVATE", "Pump", "PerCft", 99m);

        var seenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges", ApiFactory.Json);
        var get = await b.Admin.Client.GetAsync($"/api/service-charges/{charge.Id}");
        var put = await b.Admin.Client.PutAsJsonAsync($"/api/service-charges/{charge.Id}",
            new { code = "HACK", name = "Hacked", kind = "Pump", basis = "Fixed", defaultAmount = 0 });

        Assert.Empty(seenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }
}