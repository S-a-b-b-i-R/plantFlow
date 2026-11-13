using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class CostingTests(ApiFactory factory)
{

    private static Task<HttpResponseMessage> MaterialCostAsync(
        HttpClient client, Guid materialId, decimal cost, string from, Guid? plantId = null) =>
        client.PostAsJsonAsync("/api/material-costs", new { materialId, plantId, costPerUnit = cost, effectiveFrom = from });

    private static Task<HttpResponseMessage> OverheadAsync(
        HttpClient client, string kind, decimal amount, Guid? plantId = null, string from = "2026-01-01", string? to = null) =>
        client.PostAsJsonAsync("/api/overhead-costs",
            new { kind, name = $"{kind} cost", plantId, amountPerCft = amount, validFrom = from, validTo = to });

    private static Task<HttpResponseMessage> BandAsync(
        HttpClient client, decimal fromKm, decimal? toKm, decimal charge, Guid? plantId = null) =>
        client.PostAsJsonAsync("/api/delivery-charges", new { plantId, fromKm, toKm, chargePerCft = charge });

    private static async Task<HttpResponseMessage> FloorResponseAsync(
        HttpClient client, Guid productId, Guid plantId, string date, decimal km = 0, Guid? customerId = null)
    {
        var url = $"/api/floor-price?productId={productId}&plantId={plantId}&date={date}"
                  + $"&distanceKm={km.ToString(CultureInfo.InvariantCulture)}";
        if (customerId is not null) url += $"&customerId={customerId}";
        return await client.GetAsync(url);
    }

    private static async Task<FloorPriceBreakdown> FloorAsync(
        HttpClient client, Guid productId, Guid plantId, string date, decimal km = 0, Guid? customerId = null)
    {
        var response = await FloorResponseAsync(client, productId, plantId, date, km, customerId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FloorPriceBreakdown>(ApiFactory.Json))!;
    }

    // One product with a cement-only recipe of 10 kg per CFT, so the numbers stay easy to follow.
    private record Simple(Guid ProductId, Guid Cement);

    private static async Task<Simple> SimpleSetupAsync(HttpClient client)
    {
        var productId = await CreateProductAsync(client);
        var cement = await CreateMaterialAsync(client, "CEM", "Cement", "Kg");
        await CreateApprovedDesignAsync(client, productId, new[] { new { materialId = cement, quantityPerCft = 10m } });
        return new Simple(productId, cement);
    }

    [Fact]
    public async Task The_floor_price_adds_up_every_cost_component()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        var sand = await CreateMaterialAsync(admin, "SAND", "Sand", "Kg");
        var stone = await CreateMaterialAsync(admin, "STONE", "Stone", "Kg");
        var water = await CreateMaterialAsync(admin, "WATER", "Water", "Litre");
        await CreateApprovedDesignAsync(admin, productId, new[]
        {
            new { materialId = cement, quantityPerCft = 10m }, new { materialId = sand, quantityPerCft = 15m },
            new { materialId = stone, quantityPerCft = 30m }, new { materialId = water, quantityPerCft = 5m }
        });

        // 10x12 + 15x2 + 30x3 + 5x0.2 = 241
        foreach (var (material, cost) in new[] { (cement, 12m), (sand, 2m), (stone, 3m), (water, 0.2m) })
        {
            Assert.Equal(HttpStatusCode.Created, (await MaterialCostAsync(admin, material, cost, "2026-01-01")).StatusCode);
        }

        var withBridge = await CreatePlantAsync(admin, "P1");
        var withoutBridge = await CreatePlantAsync(admin, "P2");
        Assert.Equal(HttpStatusCode.Created, (await OverheadAsync(admin, "Overhead", 8m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await OverheadAsync(admin, "Fuel", 2m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await OverheadAsync(admin, "BridgeToll", 2m, withBridge)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await BandAsync(admin, 15m, 30m, 2m)).StatusCode);

        (await admin.PutAsJsonAsync("/api/pricing-policy", new
        {
            vatPercent = 15m,
            minMarginPercent = 10m,
            aitRates = new[] { new { customerType = "Contractor", percent = 5m } }
        })).EnsureSuccessStatusCode();

        var customerResponse = await admin.PostAsJsonAsync("/api/customers",
            new { code = "C1", name = "Test Customer", type = "Contractor", phone = "01700000000" });
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!;

        // Cost 241 + 8 + 2 + 2 + 2 = 255. Margin 10% and AIT 5% leave 85% of the price: 255 / 0.85 = 300.
        var floor = await FloorAsync(admin, productId, withBridge, "2026-06-01", km: 20, customerId: customer.Id);

        Assert.Equal(4, floor.Materials.Count);
        Assert.Equal(241m, floor.MaterialCost);
        Assert.Equal(8m, floor.Overhead);
        Assert.Equal(2m, floor.Fuel);
        Assert.Equal(2m, floor.BridgeToll);
        Assert.Equal(2m, floor.Delivery);
        Assert.Equal(255m, floor.TotalCost);
        Assert.Equal(5m, floor.AitPercent);
        Assert.Equal(300m, floor.FloorPrice);
        Assert.Equal(45m, floor.Vat);
        Assert.Equal(345m, floor.FloorPriceWithVat);

        // The plant without a bridge pays no toll: 253 / 0.85 = 297.647..., rounded up to 297.65.
        var noBridge = await FloorAsync(admin, productId, withoutBridge, "2026-06-01", km: 20, customerId: customer.Id);
        Assert.Equal(0m, noBridge.BridgeToll);
        Assert.Equal(297.65m, noBridge.FloorPrice);
    }

    [Fact]
    public async Task Plant_costs_beat_general_costs_and_the_latest_date_wins()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SimpleSetupAsync(admin);
        var p1 = await CreatePlantAsync(admin, "P1");
        var p2 = await CreatePlantAsync(admin, "P2");

        Assert.Equal(HttpStatusCode.Created, (await MaterialCostAsync(admin, setup.Cement, 12m, "2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await MaterialCostAsync(admin, setup.Cement, 14m, "2026-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await MaterialCostAsync(admin, setup.Cement, 13m, "2026-01-01", p1)).StatusCode);

        Assert.Equal(130m, (await FloorAsync(admin, setup.ProductId, p1, "2026-07-01")).MaterialCost);
        Assert.Equal(140m, (await FloorAsync(admin, setup.ProductId, p2, "2026-07-01")).MaterialCost);
        Assert.Equal(120m, (await FloorAsync(admin, setup.ProductId, p2, "2026-03-01")).MaterialCost);

        // Before any cost existed there is nothing to calculate with.
        var tooEarly = await FloorResponseAsync(admin, setup.ProductId, p2, "2025-12-31");
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
    }

    [Fact]
    public async Task A_missing_recipe_or_cost_gives_a_clear_conflict()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var productId = await CreateProductAsync(admin);
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        var plantId = await CreatePlantAsync(admin, "P1");

        var noDesign = await FloorResponseAsync(admin, productId, plantId, "2026-06-01");
        Assert.Equal(HttpStatusCode.Conflict, noDesign.StatusCode);

        await CreateApprovedDesignAsync(admin, productId, new[] { new { materialId = cement, quantityPerCft = 10m } });
        var noCost = await FloorResponseAsync(admin, productId, plantId, "2026-06-01");
        Assert.Equal(HttpStatusCode.Conflict, noCost.StatusCode);

        await MaterialCostAsync(admin, cement, 12m, "2026-01-01");
        Assert.Equal(HttpStatusCode.OK, (await FloorResponseAsync(admin, productId, plantId, "2026-06-01")).StatusCode);

        var unknownProduct = await FloorResponseAsync(admin, Guid.NewGuid(), plantId, "2026-06-01");
        Assert.Equal(HttpStatusCode.BadRequest, unknownProduct.StatusCode);
    }

    [Fact]
    public async Task Delivery_bands_follow_the_distance_and_cannot_overlap()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SimpleSetupAsync(admin);
        var p1 = await CreatePlantAsync(admin, "P1");
        var p2 = await CreatePlantAsync(admin, "P2");
        await MaterialCostAsync(admin, setup.Cement, 12m, "2026-01-01");

        Assert.Equal(HttpStatusCode.Created, (await BandAsync(admin, 15m, 30m, 2m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await BandAsync(admin, 30m, null, 5m)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await BandAsync(admin, 20m, 40m, 3m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BandAsync(admin, 40m, null, 3m)).StatusCode);

        async Task<decimal> DeliveryAsync(Guid plant, decimal km) =>
            (await FloorAsync(admin, setup.ProductId, plant, "2026-06-01", km)).Delivery;

        Assert.Equal(0m, await DeliveryAsync(p2, 10m));
        Assert.Equal(0m, await DeliveryAsync(p2, 15m));     // free up to and including 15 km
        Assert.Equal(2m, await DeliveryAsync(p2, 15.5m));
        Assert.Equal(2m, await DeliveryAsync(p2, 30m));     // 30 still belongs to the first band
        Assert.Equal(5m, await DeliveryAsync(p2, 45m));

        // A plant with its own bands ignores the general ones.
        Assert.Equal(HttpStatusCode.Created, (await BandAsync(admin, 0m, null, 1m, p1)).StatusCode);
        Assert.Equal(1m, await DeliveryAsync(p1, 45m));
    }

    [Fact]
    public async Task Invalid_cost_input_is_rejected()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        var plantId = await CreatePlantAsync(admin, "P1");

        Assert.Equal(HttpStatusCode.Created, (await MaterialCostAsync(admin, cement, 12m, "2026-01-01")).StatusCode);
        var duplicate = await MaterialCostAsync(admin, cement, 13m, "2026-01-01");
        var unknownMaterial = await MaterialCostAsync(admin, Guid.NewGuid(), 12m, "2026-01-01");
        var zeroCost = await MaterialCostAsync(admin, cement, 0m, "2026-02-01");
        var tooPrecise = await MaterialCostAsync(admin, cement, 1.23456m, "2026-02-01");
        var missingDate = await admin.PostAsJsonAsync("/api/material-costs", new { materialId = cement, costPerUnit = 5m });

        var bridgeWithoutPlant = await OverheadAsync(admin, "BridgeToll", 2m);
        var endsBeforeStart = await OverheadAsync(admin, "Overhead", 2m, plantId, "2026-06-01", "2026-05-01");
        var zeroAmount = await OverheadAsync(admin, "Fuel", 0m);

        var upperBelowLower = await BandAsync(admin, 30m, 20m, 2m);
        var upperEqualsLower = await BandAsync(admin, 30m, 30m, 2m);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.All(
            new[] { unknownMaterial, zeroCost, tooPrecise, missingDate, bridgeWithoutPlant, endsBeforeStart, zeroAmount, upperBelowLower, upperEqualsLower },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task Cost_rows_can_be_corrected_and_removed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");

        var first = await MaterialCostAsync(admin, cement, 12m, "2026-01-01");
        var second = await MaterialCostAsync(admin, cement, 14m, "2026-06-01");
        var firstCost = (await first.Content.ReadFromJsonAsync<MaterialCostDto>(ApiFactory.Json))!;
        var secondCost = (await second.Content.ReadFromJsonAsync<MaterialCostDto>(ApiFactory.Json))!;

        var fix = await admin.PutAsJsonAsync($"/api/material-costs/{firstCost.Id}", new { costPerUnit = 12.5m, effectiveFrom = "2026-01-01" });
        var clash = await admin.PutAsJsonAsync($"/api/material-costs/{firstCost.Id}", new { costPerUnit = 12.5m, effectiveFrom = "2026-06-01" });
        var fixed12 = await admin.GetFromJsonAsync<MaterialCostDto>($"/api/material-costs/{firstCost.Id}", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NoContent, fix.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(12.5m, fixed12!.CostPerUnit);

        var overhead = await OverheadAsync(admin, "Overhead", 8m);
        var overheadId = (await overhead.Content.ReadFromJsonAsync<OverheadCostDto>(ApiFactory.Json))!.Id;
        var band = await BandAsync(admin, 15m, 30m, 2m);
        var bandId = (await band.Content.ReadFromJsonAsync<DeliveryChargeBandDto>(ApiFactory.Json))!.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/material-costs/{secondCost.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/overhead-costs/{overheadId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/delivery-charges/{bandId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/material-costs/{secondCost.Id}")).StatusCode);

        var remaining = await admin.GetFromJsonAsync<PagedResult<MaterialCostDto>>("/api/material-costs", ApiFactory.Json);
        Assert.Equal(firstCost.Id, Assert.Single(remaining!.Items).Id);
    }

    [Fact]
    public async Task The_pricing_policy_has_defaults_and_can_be_changed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        var defaults = await admin.GetFromJsonAsync<PricingPolicyDto>("/api/pricing-policy", ApiFactory.Json);
        Assert.Equal(15m, defaults!.VatPercent);
        Assert.Equal(10m, defaults.MinMarginPercent);
        Assert.Empty(defaults.AitRates);

        var save = await admin.PutAsJsonAsync("/api/pricing-policy", new
        {
            vatPercent = 7.5m,
            minMarginPercent = 12m,
            aitRates = new[] { new { customerType = "Contractor", percent = 5m }, new { customerType = "Developer", percent = 7m } }
        });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        // Saving again replaces the list and keeps one row per type.
        var again = await admin.PutAsJsonAsync("/api/pricing-policy", new
        {
            vatPercent = 7.5m,
            minMarginPercent = 12m,
            aitRates = new[] { new { customerType = "Developer", percent = 6m } }
        });
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        var saved = await admin.GetFromJsonAsync<PricingPolicyDto>("/api/pricing-policy", ApiFactory.Json);
        Assert.Equal(7.5m, saved!.VatPercent);
        Assert.Equal(12m, saved.MinMarginPercent);
        var rate = Assert.Single(saved.AitRates);
        Assert.Equal(6m, rate.Percent);

        var duplicateType = await admin.PutAsJsonAsync("/api/pricing-policy", new
        {
            vatPercent = 15m,
            minMarginPercent = 10m,
            aitRates = new[] { new { customerType = "Developer", percent = 1m }, new { customerType = "Developer", percent = 2m } }
        });
        var marginTooHigh = await admin.PutAsJsonAsync("/api/pricing-policy",
            new { vatPercent = 15m, minMarginPercent = 70m, aitRates = Array.Empty<object>() });
        var tooPrecise = await admin.PutAsJsonAsync("/api/pricing-policy",
            new { vatPercent = 15.123m, minMarginPercent = 10m, aitRates = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, duplicateType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, marginTooHigh.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
    }

    [Fact]
    public async Task Only_cost_managers_can_see_costs_and_companies_are_separate()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var setup = await SimpleSetupAsync(a.Admin.Client);
        var plantId = await CreatePlantAsync(a.Admin.Client, "P1");
        await MaterialCostAsync(a.Admin.Client, setup.Cement, 12m, "2026-01-01");
        await OverheadAsync(a.Admin.Client, "Overhead", 8m);

        var rep = await factory.AddMemberAsync(a, "Sales Representative"); // no costs.manage
        var costsAsRep = await rep.Client.GetAsync("/api/material-costs");
        var policyAsRep = await rep.Client.GetAsync("/api/pricing-policy");
        var floorAsRep = await FloorResponseAsync(rep.Client, setup.ProductId, plantId, "2026-06-01");

        var seenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<MaterialCostDto>>("/api/material-costs", ApiFactory.Json);
        var overheadsSeenByB = await b.Admin.Client.GetFromJsonAsync<PagedResult<OverheadCostDto>>("/api/overhead-costs", ApiFactory.Json);
        var floorAsB = await FloorResponseAsync(b.Admin.Client, setup.ProductId, plantId, "2026-06-01");

        Assert.Equal(HttpStatusCode.Forbidden, costsAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, policyAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, floorAsRep.StatusCode);
        Assert.Empty(seenByB!.Items);
        Assert.Empty(overheadsSeenByB!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, floorAsB.StatusCode);
    }
}