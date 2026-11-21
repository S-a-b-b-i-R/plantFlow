using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

// Technical manages products and materials, Accounts manages service charges. Deactivating something
// still in use is allowed after a warning, and codes stay editable.
[Collection("api")]
public class CatalogueTests(ApiFactory factory)
{
    private static object ProductBody(string code, string status = "Active") =>
        new { code, name = $"Grade {code}", strengthClass = "M25", maxAggregateMm = 20, defaultSlumpMm = 100, status };

    private static object ChargeBody(string code) =>
        new { code, name = "Night delivery", kind = "NightDelivery", basis = "PerLoad", defaultAmount = 500m };

    [Fact]
    public async Task Technical_manages_products_and_materials_and_accounts_manages_charges()
    {
        var company = await factory.CreateCompanyAsync();
        var technical = (await factory.AddMemberAsync(company, "Technical")).Client;
        var accounts = (await factory.AddMemberAsync(company, "Accounts")).Client;
        var planner = (await factory.AddMemberAsync(company, "Planner")).Client;
        var material = new { code = "CEM", name = "Cement", category = "Cement", unit = "Kg" };

        Assert.Equal(HttpStatusCode.Created, (await technical.PostAsJsonAsync("/api/products", ProductBody("G1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await technical.PostAsJsonAsync("/api/materials", material)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technical.PostAsJsonAsync("/api/service-charges", ChargeBody("NIGHT"))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await accounts.PostAsJsonAsync("/api/service-charges", ChargeBody("NIGHT"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accounts.PostAsJsonAsync("/api/products", ProductBody("G2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accounts.PostAsJsonAsync("/api/materials", material)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await planner.PostAsJsonAsync("/api/products", ProductBody("G3"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Usage_counts_what_still_uses_a_product_or_material_and_deactivating_is_still_allowed()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin); // An approved mix design using cement, and an active price.
        await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 50m)]);
        var materials = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials", ApiFactory.Json);
        var cement = materials!.Items.Single(m => m.Code == "CEM").Id;

        var product = await admin.GetFromJsonAsync<ProductUsageDto>($"/api/products/{w.ProductId}/usage", ApiFactory.Json);
        var material = await admin.GetFromJsonAsync<MaterialUsageDto>($"/api/materials/{cement}/usage", ApiFactory.Json);
        Assert.Equal(new ProductUsageDto(1, 1, 1), product);
        Assert.Equal(new MaterialUsageDto(1), material);

        // Deactivating and renaming the code both go through.
        var deactivated = await admin.PutAsJsonAsync($"/api/products/{w.ProductId}", ProductBody("G4000-OLD", "Inactive"));
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        var after = await admin.GetFromJsonAsync<ProductDto>($"/api/products/{w.ProductId}", ApiFactory.Json);
        Assert.Equal("G4000-OLD", after!.Code);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/products/{w.ProductId}/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/materials/{cement}/usage")).StatusCode);
    }
}
