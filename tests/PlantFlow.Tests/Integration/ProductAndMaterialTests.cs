using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class ProductAndMaterialTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> PostMaterialAsync(
        HttpClient client, string code, string name = "Test Material", string category = "Cement", string unit = "Kg",
        string status = "Active") =>
        client.PostAsJsonAsync("/api/materials", new { code, name, category, unit, status });

    private static Task<HttpResponseMessage> PostProductAsync(
        HttpClient client, string code, string name = "Test Product", string strength = "M25",
        int aggregate = 20, int slump = 100, string status = "Active") =>
        client.PostAsJsonAsync("/api/products",
            new { code, name, strengthClass = strength, maxAggregateMm = aggregate, defaultSlumpMm = slump, status });

    private static async Task<T> ReadCreatedAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    // ---- Materials ----

    [Fact]
    public async Task A_material_is_created_with_a_normalized_code_category_and_unit()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var material = await ReadCreatedAsync<MaterialDto>(
            await PostMaterialAsync(admin, "opc-1", "Ordinary Portland Cement", "Cement", "Kg"));
        var water = await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(admin, "w-1", "Water", "Water", "Litre"));

        Assert.Equal("OPC-1", material.Code);
        Assert.Equal(PlantFlow.Api.Domain.MasterData.MaterialCategory.Cement, material.Category);
        Assert.Equal(PlantFlow.Api.Domain.MasterData.MaterialUnit.Litre, water.Unit);
    }

    [Fact]
    public async Task Material_input_is_validated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var badCategory = await admin.PostAsJsonAsync("/api/materials",
            new { code = "X1", name = "Mystery", category = "Gold", unit = "Kg" });
        var badUnit = await admin.PostAsJsonAsync("/api/materials",
            new { code = "X2", name = "Mystery", category = "Sand", unit = "Bucket" });
        var badCode = await PostMaterialAsync(admin, "has space");
        var noName = await admin.PostAsJsonAsync("/api/materials", new { code = "X3", category = "Sand", unit = "Kg" });

        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badUnit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        var noCategory = await admin.PostAsJsonAsync("/api/materials", new { code = "X4", name = "No category", unit = "Kg" });
        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
    }

    [Fact]
    public async Task Material_codes_are_unique_within_a_company_only()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();

        await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(a.Admin.Client, "SAND-1", category: "Sand"));

        var duplicate = await PostMaterialAsync(a.Admin.Client, "sand-1", category: "Sand");
        var otherCompany = await PostMaterialAsync(b.Admin.Client, "SAND-1", category: "Sand");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCompany.StatusCode);
    }

    [Fact]
    public async Task The_material_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(admin, "C1", "Cement OPC", "Cement"));
        await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(admin, "S1", "River Sand", "Sand"));
        await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(admin, "S2", "Coarse Sand", "Sand"));
        await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(admin, "A1", "Old Admixture", "Admixture", status: "Inactive"));

        var firstPage = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials?pageSize=3", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials?pageSize=3&page=2", ApiFactory.Json);
        var sands = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials?category=Sand", ApiFactory.Json);
        var found = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials?search=river", ApiFactory.Json);
        var inactive = await admin.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials?status=Inactive", ApiFactory.Json);

        Assert.Equal(4, firstPage!.TotalCount);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal(2, sands!.TotalCount);
        Assert.Equal("S1", Assert.Single(found!.Items).Code);
        Assert.Equal("A1", Assert.Single(inactive!.Items).Code);
    }

    // ---- Products ----

    [Fact]
    public async Task A_product_is_created_with_normalized_codes()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var product = await ReadCreatedAsync<ProductDto>(await PostProductAsync(admin, "m25-20", "M25 20mm", "m25", 20, 100));

        Assert.Equal("M25-20", product.Code);
        Assert.Equal("M25", product.StrengthClass);
        Assert.Equal(20, product.MaxAggregateMm);
        Assert.Equal(100, product.DefaultSlumpMm);
    }

    [Fact]
    public async Task Product_input_is_validated()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var tinyAggregate = await PostProductAsync(admin, "P1", aggregate: 1);
        var hugeSlump = await PostProductAsync(admin, "P2", slump: 500);
        var noStrength = await PostProductAsync(admin, "P3", strength: "");
        var badCode = await PostProductAsync(admin, "bad code");

        Assert.Equal(HttpStatusCode.BadRequest, tinyAggregate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, hugeSlump.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noStrength.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
    }

    [Fact]
    public async Task Product_codes_are_unique_within_a_company_only()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();

        await ReadCreatedAsync<ProductDto>(await PostProductAsync(a.Admin.Client, "M30"));

        var duplicate = await PostProductAsync(a.Admin.Client, "m30");
        var otherCompany = await PostProductAsync(b.Admin.Client, "M30");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCompany.StatusCode);
    }

    [Fact]
    public async Task The_product_list_supports_paging_search_and_sorting_and_updates_stick()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var m20 = await ReadCreatedAsync<ProductDto>(await PostProductAsync(admin, "P20", "Grade twenty", "M20"));
        await ReadCreatedAsync<ProductDto>(await PostProductAsync(admin, "P25", "Grade twenty-five", "M25"));
        await ReadCreatedAsync<ProductDto>(await PostProductAsync(admin, "P30", "Grade thirty", "M30"));

        var firstPage = await admin.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?pageSize=2&page=2", ApiFactory.Json);
        var byStrength = await admin.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=m30", ApiFactory.Json);
        var descending = await admin.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?sort=-strength", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("P30", Assert.Single(byStrength!.Items).Code);
        Assert.Equal("M30", descending!.Items[0].StrengthClass);

        var update = await admin.PutAsJsonAsync($"/api/products/{m20.Id}", new
        {
            code = "P20",
            name = "Grade twenty",
            strengthClass = "M20",
            maxAggregateMm = 40,
            defaultSlumpMm = 120,
            status = "Inactive"
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var reloaded = await admin.GetFromJsonAsync<ProductDto>($"/api/products/{m20.Id}", ApiFactory.Json);
        Assert.Equal(40, reloaded!.MaxAggregateMm);
        Assert.Equal(PlantFlow.Api.Domain.MasterData.ActiveStatus.Inactive, reloaded.Status);
    }

    // ---- Permissions and isolation ----

    [Fact]
    public async Task View_permission_can_read_but_not_write()
    {
        var company = await factory.CreateCompanyAsync();
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var materials = await rep.Client.GetAsync("/api/materials");
        var products = await rep.Client.GetAsync("/api/products");
        var createMaterial = await PostMaterialAsync(rep.Client, "NOPE");
        var createProduct = await PostProductAsync(rep.Client, "NOPE");

        Assert.Equal(HttpStatusCode.OK, materials.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createMaterial.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createProduct.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_products_or_materials()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var material = await ReadCreatedAsync<MaterialDto>(await PostMaterialAsync(a.Admin.Client, "PRIVATE"));
        var product = await ReadCreatedAsync<ProductDto>(await PostProductAsync(a.Admin.Client, "PRIVATE"));

        var materialsSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<MaterialDto>>("/api/materials", ApiFactory.Json);
        var productsSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products", ApiFactory.Json);
        var getMaterial = await b.Admin.Client.GetAsync($"/api/materials/{material.Id}");
        var getProduct = await b.Admin.Client.GetAsync($"/api/products/{product.Id}");
        var putMaterial = await b.Admin.Client.PutAsJsonAsync($"/api/materials/{material.Id}",
            new { code = "HACK", name = "Hacked", category = "Sand", unit = "Kg" });
        var putProduct = await b.Admin.Client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { code = "HACK", name = "Hacked", strengthClass = "M10", maxAggregateMm = 20, defaultSlumpMm = 100 });

        Assert.Empty(materialsSeenByB!.Items);
        Assert.Empty(productsSeenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, getMaterial.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getProduct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, putMaterial.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, putProduct.StatusCode);
    }
}