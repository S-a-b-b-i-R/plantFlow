using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class CostMarginTests(ApiFactory factory)
{
    private record Setup(Guid PlantId, Guid Cement);

    // Cement costs 8 per kg and the recipe uses 10 kg per CFT: material cost 80. Overhead 10. Total cost 90.
    // With the default 10% margin the floor price is 90 / 0.9 = 100.
    private static async Task<Setup> SetupAsync(HttpClient admin)
    {
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        var plantId = await CreatePlantAsync(admin, "P1");

        (await admin.PostAsJsonAsync("/api/material-costs",
            new { materialId = cement, costPerUnit = 8m, effectiveFrom = "2026-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/overhead-costs",
            new { kind = "Overhead", name = "Labour", amountPerCft = 10m, validFrom = "2026-01-01" })).EnsureSuccessStatusCode();

        var items = new[] { new { materialId = cement, quantityPerCft = 10m } };
        var alpha = await CreateProductAsync(admin, "ALPHA");
        var bravo = await CreateProductAsync(admin, "BRAVO");
        var charlie = await CreateProductAsync(admin, "CHARLIE");
        await CreateProductAsync(admin, "DELTA");                    // no recipe at all
        await CreateProductAsync(admin, "ECHO", "Inactive");         // inactive products are left out

        await CreateApprovedDesignAsync(admin, alpha, items);
        await CreateApprovedDesignAsync(admin, bravo, items);
        await CreateApprovedDesignAsync(admin, charlie, items);

        await CreateActivePriceAsync(admin, alpha, 120m);
        await CreateActivePriceAsync(admin, bravo, 95m);             // below the floor of 100
        return new Setup(plantId, cement);
    }

    private static async Task<List<CostMarginRowDto>> ReportAsync(
        HttpClient client, Guid plantId, decimal km = 0, Guid? customerId = null)
    {
        var url = $"/api/cost-margin?plantId={plantId}&date=2026-06-01&distanceKm={km.ToString(CultureInfo.InvariantCulture)}"
                  + (customerId is null ? "" : $"&customerId={customerId}");
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CostMarginRowDto>>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task The_report_shows_cost_floor_price_and_margin_per_product()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SetupAsync(admin);

        var rows = await ReportAsync(admin, setup.PlantId);

        Assert.Equal(["ALPHA", "BRAVO", "CHARLIE", "DELTA"], rows.Select(r => r.ProductCode));

        // Price 120 on a cost of 90: margin 30, which is 25% of the price. The floor of 100 is not breached.
        var alpha = rows[0];
        Assert.Equal(90m, alpha.TotalCost);
        Assert.Equal(100m, alpha.FloorPrice);
        Assert.Equal(120m, alpha.ActivePrice);
        Assert.Equal(PriceScope.General, alpha.PriceScope);
        Assert.Equal(30m, alpha.MarginAmount);
        Assert.Equal(25m, alpha.MarginPercent);
        Assert.False(alpha.BelowFloor);
        Assert.Null(alpha.Note);

        // Price 95 is under the floor: margin 5 is 5.26% of the price.
        var bravo = rows[1];
        Assert.Equal(5m, bravo.MarginAmount);
        Assert.Equal(5.26m, bravo.MarginPercent);
        Assert.True(bravo.BelowFloor);

        // A recipe and costs but no price: the cost side is known, the margin is not.
        var charlie = rows[2];
        Assert.Equal(100m, charlie.FloorPrice);
        Assert.Null(charlie.ActivePrice);
        Assert.Null(charlie.MarginAmount);
        Assert.Contains("no active price", charlie.Note);

        // No recipe: nothing can be worked out, and the note says why.
        var delta = rows[3];
        Assert.Null(delta.TotalCost);
        Assert.Null(delta.FloorPrice);
        Assert.Contains("mix design", delta.Note);
    }

    [Fact]
    public async Task A_customer_brings_their_ait_and_their_own_price()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SetupAsync(admin);

        (await admin.PutAsJsonAsync("/api/pricing-policy", new
        {
            vatPercent = 15m,
            minMarginPercent = 10m,
            aitRates = new[] { new { customerType = "Contractor", percent = 10m } }
        })).EnsureSuccessStatusCode();

        var customerId = await CreateCustomerAsync(admin, "C1");
        var alphaId = (await ReportAsync(admin, setup.PlantId)).First(r => r.ProductCode == "ALPHA").ProductId;
        await CreateActivePriceAsync(admin, alphaId, 125m, customerId: customerId);

        var alpha = (await ReportAsync(admin, setup.PlantId, customerId: customerId)).First(r => r.ProductCode == "ALPHA");

        // Margin 10% and AIT 10% leave 80% of the price: floor = 90 / 0.8 = 112.5.
        Assert.Equal(112.5m, alpha.FloorPrice);
        Assert.Equal(125m, alpha.ActivePrice);
        Assert.Equal(PriceScope.Customer, alpha.PriceScope);

        // 125 less 10% AIT is 112.5, less cost 90 is 22.5, which is 18% of the price.
        Assert.Equal(22.5m, alpha.MarginAmount);
        Assert.Equal(18m, alpha.MarginPercent);
        Assert.False(alpha.BelowFloor);
    }

    [Fact]
    public async Task Distance_changes_the_cost_through_the_delivery_bands()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SetupAsync(admin);

        (await admin.PostAsJsonAsync("/api/delivery-charges",
            new { fromKm = 15m, toKm = 30m, chargePerCft = 9m })).EnsureSuccessStatusCode();

        var near = (await ReportAsync(admin, setup.PlantId, km: 10m)).First(r => r.ProductCode == "ALPHA");
        var far = (await ReportAsync(admin, setup.PlantId, km: 20m)).First(r => r.ProductCode == "ALPHA");

        Assert.Equal(90m, near.TotalCost);

        // Cost 99: floor 99 / 0.9 = 110, margin 120 - 99 = 21, which is 17.5% of the price.
        Assert.Equal(99m, far.TotalCost);
        Assert.Equal(110m, far.FloorPrice);
        Assert.Equal(21m, far.MarginAmount);
        Assert.Equal(17.5m, far.MarginPercent);
    }

    [Fact]
    public async Task Only_cost_managers_can_use_it_and_input_is_checked()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var setup = await SetupAsync(company.Admin.Client);
        var foreignPlant = await CreatePlantAsync(other.Admin.Client, "PX");

        var rep = await factory.AddMemberAsync(company, "Sales Representative"); // no costs.manage
        var asRep = await rep.Client.GetAsync($"/api/cost-margin?plantId={setup.PlantId}");

        var negativeDistance = await company.Admin.Client.GetAsync($"/api/cost-margin?plantId={setup.PlantId}&distanceKm=-1");
        var unknownPlant = await company.Admin.Client.GetAsync($"/api/cost-margin?plantId={Guid.NewGuid()}");
        var otherCompanysPlant = await company.Admin.Client.GetAsync($"/api/cost-margin?plantId={foreignPlant}");
        var unknownCustomer = await company.Admin.Client.GetAsync(
            $"/api/cost-margin?plantId={setup.PlantId}&customerId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, asRep.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativeDistance.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownPlant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherCompanysPlant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownCustomer.StatusCode);
    }
}