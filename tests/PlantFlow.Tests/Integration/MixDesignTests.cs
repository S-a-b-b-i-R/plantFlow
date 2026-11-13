using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;
namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class MixDesignTests(ApiFactory factory)
{
    private record Catalog(Guid ProductId, Guid Cement, Guid Sand, Guid Stone, Guid Water);

    private static async Task<Catalog> CreateCatalogAsync(HttpClient client, string productCode = "G4000-20") => new(
        await CreateProductAsync(client, productCode),
        await CreateMaterialAsync(client, "CEM", "Cement", "Kg"),
        await CreateMaterialAsync(client, "SAND", "Sand", "Kg"),
        await CreateMaterialAsync(client, "STONE", "Stone", "Kg"),
        await CreateMaterialAsync(client, "WATER", "Water", "Litre"));

    private static object StandardItems(Catalog c, decimal cement = 10m) => new[]
    {
        new { materialId = c.Cement, quantityPerCft = cement },
        new { materialId = c.Sand, quantityPerCft = 15m },
        new { materialId = c.Stone, quantityPerCft = 30m },
        new { materialId = c.Water, quantityPerCft = 5m }
    };

    private static Task<HttpResponseMessage> PostDesignAsync(
        HttpClient client, Guid productId, object items, Guid? plantId = null, string? notes = null) =>
        client.PostAsJsonAsync("/api/mix-designs", new { productId, plantId, notes, items });

    private static async Task<MixDesignDetailDto> CreateDesignAsync(
        HttpClient client, Guid productId, object items, Guid? plantId = null)
    {
        var response = await PostDesignAsync(client, productId, items, plantId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MixDesignDetailDto>(ApiFactory.Json))!;
    }

    private static async Task<HttpResponseMessage> ActAsync(HttpClient client, Guid id, string action) =>
        await client.PostAsync($"/api/mix-designs/{id}/{action}", null);

    private static async Task ApproveAsync(HttpClient client, Guid id)
    {
        (await ActAsync(client, id, "submit")).EnsureSuccessStatusCode();
        (await ActAsync(client, id, "approve")).EnsureSuccessStatusCode();
    }

    private static Task<MixDesignDetailDto?> GetDesignAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<MixDesignDetailDto>($"/api/mix-designs/{id}", ApiFactory.Json);

    [Fact]
    public async Task A_design_goes_from_draft_to_approved()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);

        var draft = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        Assert.Equal(1, draft.Design.Version);
        Assert.Equal(MixDesignStatus.Draft, draft.Design.Status);
        Assert.Equal(4, draft.Items.Count);
        Assert.Equal(["CEM", "SAND", "STONE", "WATER"], draft.Items.Select(i => i.MaterialCode));

        await ApproveAsync(admin, draft.Design.Id);

        var approved = await GetDesignAsync(admin, draft.Design.Id);
        Assert.Equal(MixDesignStatus.Approved, approved!.Design.Status);
        Assert.NotNull(approved.Design.ApprovedAtUtc);
    }

    [Fact]
    public async Task Only_a_draft_can_be_edited()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);
        var admixture = await CreateMaterialAsync(admin, "ADM", "Admixture", "Litre");
        var draft = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));

        // Drop sand, change cement, add an admixture.
        var edited = new
        {
            notes = "Trial 2",
            items = new[]
            {
                new { materialId = catalog.Cement, quantityPerCft = 11m },
                new { materialId = catalog.Stone, quantityPerCft = 30m },
                new { materialId = catalog.Water, quantityPerCft = 5m },
                new { materialId = admixture, quantityPerCft = 0.05m }
            }
        };
        var update = await admin.PutAsJsonAsync($"/api/mix-designs/{draft.Design.Id}", edited);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var reloaded = await GetDesignAsync(admin, draft.Design.Id);
        Assert.Equal("Trial 2", reloaded!.Design.Notes);
        Assert.DoesNotContain(reloaded.Items, i => i.MaterialCode == "SAND");
        Assert.Equal(11m, reloaded.Items.Single(i => i.MaterialCode == "CEM").QuantityPerCft);
        Assert.Contains(reloaded.Items, i => i.MaterialCode == "ADM");

        (await ActAsync(admin, draft.Design.Id, "submit")).EnsureSuccessStatusCode();
        var whilePending = await admin.PutAsJsonAsync($"/api/mix-designs/{draft.Design.Id}", edited);
        (await ActAsync(admin, draft.Design.Id, "approve")).EnsureSuccessStatusCode();
        var afterApproval = await admin.PutAsJsonAsync($"/api/mix-designs/{draft.Design.Id}", edited);

        Assert.Equal(HttpStatusCode.Conflict, whilePending.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, afterApproval.StatusCode);
    }

    [Fact]
    public async Task Approving_a_new_version_retires_the_previous_one()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);

        var v1 = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        await ApproveAsync(admin, v1.Design.Id);

        var v2 = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog, cement: 10.5m));
        Assert.Equal(2, v2.Design.Version);
        Assert.Equal(MixDesignStatus.Draft, v2.Design.Status);

        // v1 stays approved until v2 is approved.
        Assert.Equal(MixDesignStatus.Approved, (await GetDesignAsync(admin, v1.Design.Id))!.Design.Status);

        await ApproveAsync(admin, v2.Design.Id);

        Assert.Equal(MixDesignStatus.Retired, (await GetDesignAsync(admin, v1.Design.Id))!.Design.Status);
        Assert.Equal(MixDesignStatus.Approved, (await GetDesignAsync(admin, v2.Design.Id))!.Design.Status);

        var audit = await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit?take=100", ApiFactory.Json);
        Assert.Contains(audit!, e => e.Action == "mixdesign.retired" && e.EntityId == v1.Design.Id.ToString());
    }

    [Fact]
    public async Task There_is_one_open_version_per_product_and_plant_and_plants_are_separate_scopes()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);
        var plantId = await CreatePlantAsync(admin, "P1");

        var general = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        var secondGeneral = await PostDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        Assert.Equal(HttpStatusCode.Conflict, secondGeneral.StatusCode);

        // The same product for a specific plant is a different scope, so it is allowed.
        var plantSpecific = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog, cement: 11m), plantId);
        Assert.Equal(1, plantSpecific.Design.Version);

        await ApproveAsync(admin, general.Design.Id);
        await ApproveAsync(admin, plantSpecific.Design.Id);

        // Approving the plant recipe did not retire the general one.
        Assert.Equal(MixDesignStatus.Approved, (await GetDesignAsync(admin, general.Design.Id))!.Design.Status);
        Assert.Equal(MixDesignStatus.Approved, (await GetDesignAsync(admin, plantSpecific.Design.Id))!.Design.Status);

        // Once the open version is approved, the next version can start.
        var next = await PostDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task A_rejected_design_returns_to_draft_with_the_reason()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);
        var draft = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        (await ActAsync(admin, draft.Design.Id, "submit")).EnsureSuccessStatusCode();

        var noReason = await admin.PostAsJsonAsync($"/api/mix-designs/{draft.Design.Id}/reject", new { reason = "" });
        var reject = await admin.PostAsJsonAsync($"/api/mix-designs/{draft.Design.Id}/reject", new { reason = "Too much water" });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var rejected = await GetDesignAsync(admin, draft.Design.Id);
        Assert.Equal(MixDesignStatus.Draft, rejected!.Design.Status);
        Assert.Equal("Too much water", rejected.Design.RejectionReason);

        (await ActAsync(admin, draft.Design.Id, "submit")).EnsureSuccessStatusCode();
        var resubmitted = await GetDesignAsync(admin, draft.Design.Id);
        Assert.Null(resubmitted!.Design.RejectionReason);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var catalog = await CreateCatalogAsync(admin);
        var inactiveMaterial = await CreateMaterialAsync(admin, "OLD", "Additive", "Kg", "Inactive");
        var inactiveProduct = await CreateProductAsync(admin, "OLDPROD", "Inactive");
        var foreignPlant = await CreatePlantAsync(other.Admin.Client, "PX");

        var duplicate = await PostDesignAsync(admin, catalog.ProductId, new[]
        {
            new { materialId = catalog.Cement, quantityPerCft = 10m },
            new { materialId = catalog.Cement, quantityPerCft = 12m }
        });
        var unknownMaterial = await PostDesignAsync(admin, catalog.ProductId,
            new[] { new { materialId = Guid.NewGuid(), quantityPerCft = 10m } });
        var inactiveMat = await PostDesignAsync(admin, catalog.ProductId,
            new[] { new { materialId = inactiveMaterial, quantityPerCft = 10m } });
        var zeroQuantity = await PostDesignAsync(admin, catalog.ProductId,
            new[] { new { materialId = catalog.Cement, quantityPerCft = 0m } });
        var tooManyDecimals = await PostDesignAsync(admin, catalog.ProductId,
            new[] { new { materialId = catalog.Cement, quantityPerCft = 10.12345m } });
        var noItems = await PostDesignAsync(admin, catalog.ProductId, Array.Empty<object>());
        var inactiveProd = await PostDesignAsync(admin, inactiveProduct, StandardItems(catalog));
        var foreignPlantUsed = await PostDesignAsync(admin, catalog.ProductId, StandardItems(catalog), foreignPlant);

        Assert.All(
            new[] { duplicate, unknownMaterial, inactiveMat, zeroQuantity, tooManyDecimals, noItems, inactiveProd, foreignPlantUsed },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task Status_changes_only_follow_the_allowed_order()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin);
        var draft = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));

        var approveDraft = await ActAsync(admin, draft.Design.Id, "approve");
        var retireDraft = await ActAsync(admin, draft.Design.Id, "retire");
        var rejectDraft = await admin.PostAsJsonAsync($"/api/mix-designs/{draft.Design.Id}/reject", new { reason = "Nope nope" });

        await ApproveAsync(admin, draft.Design.Id);
        var submitApproved = await ActAsync(admin, draft.Design.Id, "submit");
        var retire = await ActAsync(admin, draft.Design.Id, "retire");
        var retireAgain = await ActAsync(admin, draft.Design.Id, "retire");

        Assert.Equal(HttpStatusCode.Conflict, approveDraft.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retireDraft.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejectDraft.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, submitApproved.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retireAgain.StatusCode);
        Assert.Equal(MixDesignStatus.Retired, (await GetDesignAsync(admin, draft.Design.Id))!.Design.Status);
    }

    [Fact]
    public async Task The_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var catalog = await CreateCatalogAsync(admin, "ALPHA");
        var bravo = await CreateProductAsync(admin, "BRAVO");
        var charlie = await CreateProductAsync(admin, "CHARLIE");

        var a = await CreateDesignAsync(admin, catalog.ProductId, StandardItems(catalog));
        await CreateDesignAsync(admin, bravo, StandardItems(catalog));
        await CreateDesignAsync(admin, charlie, StandardItems(catalog));
        await ApproveAsync(admin, a.Design.Id);

        var firstPage = await admin.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>("/api/mix-designs?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>("/api/mix-designs?pageSize=2&page=2", ApiFactory.Json);
        var byProduct = await admin.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>($"/api/mix-designs?productId={bravo}", ApiFactory.Json);
        var approved = await admin.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>("/api/mix-designs?status=Approved", ApiFactory.Json);
        var found = await admin.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>("/api/mix-designs?search=charl", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal("BRAVO", Assert.Single(byProduct!.Items).ProductCode);
        Assert.Equal("ALPHA", Assert.Single(approved!.Items).ProductCode);
        Assert.Equal("CHARLIE", Assert.Single(found!.Items).ProductCode);
    }

    [Fact]
    public async Task Editing_and_approving_need_separate_permissions()
    {
        var company = await factory.CreateCompanyAsync();
        var catalog = await CreateCatalogAsync(company.Admin.Client);
        var member = await factory.AddMemberAsync(company, "Sales Representative"); // has mixdesigns.view only

        var listAsViewer = await member.Client.GetAsync("/api/mix-designs");
        var createAsViewer = await PostDesignAsync(member.Client, catalog.ProductId, StandardItems(catalog));

        // Give the member the edit permission on top of their role.
        (await company.Admin.Client.PutAsJsonAsync($"/api/users/{member.Id}/permissions",
            new { overrides = new[] { new { permissionKey = Permissions.MixDesignsEdit, isGranted = true } } }))
            .EnsureSuccessStatusCode();

        var draft = await CreateDesignAsync(member.Client, catalog.ProductId, StandardItems(catalog));
        var submit = await ActAsync(member.Client, draft.Design.Id, "submit");
        var approveWithoutPermission = await ActAsync(member.Client, draft.Design.Id, "approve");
        var approveAsAdmin = await ActAsync(company.Admin.Client, draft.Design.Id, "approve");

        Assert.Equal(HttpStatusCode.OK, listAsViewer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createAsViewer.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approveWithoutPermission.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, approveAsAdmin.StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_change_mix_designs()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var catalog = await CreateCatalogAsync(a.Admin.Client);
        var draft = await CreateDesignAsync(a.Admin.Client, catalog.ProductId, StandardItems(catalog));
        (await ActAsync(a.Admin.Client, draft.Design.Id, "submit")).EnsureSuccessStatusCode();

        var listSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<MixDesignSummaryDto>>("/api/mix-designs", ApiFactory.Json);
        var get = await b.Admin.Client.GetAsync($"/api/mix-designs/{draft.Design.Id}");
        var approve = await ActAsync(b.Admin.Client, draft.Design.Id, "approve");
        var update = await b.Admin.Client.PutAsJsonAsync($"/api/mix-designs/{draft.Design.Id}",
            new { items = StandardItems(catalog) });
        var useForeignProduct = await PostDesignAsync(b.Admin.Client, catalog.ProductId, StandardItems(catalog));

        Assert.Empty(listSeenByB!.Items);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, useForeignProduct.StatusCode);
    }
}