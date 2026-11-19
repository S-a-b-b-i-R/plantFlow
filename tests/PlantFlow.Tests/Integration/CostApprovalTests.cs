using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class CostApprovalTests(ApiFactory factory)
{
    private static async Task<HttpResponseMessage> FloorAsync(HttpClient client, Guid productId, Guid plantId, decimal km = 0)
    {
        var url = $"/api/floor-price?productId={productId}&plantId={plantId}&date=2026-06-01"
                  + $"&distanceKm={km.ToString(CultureInfo.InvariantCulture)}";
        return await client.GetAsync(url);
    }

    private record Setup(Guid ProductId, Guid PlantId, Guid Cement);

    // A recipe of 10 kg cement per CFT. No costs yet.
    private static async Task<Setup> SetupAsync(HttpClient admin)
    {
        var productId = await CreateProductAsync(admin);
        var plantId = await CreatePlantAsync(admin, "P1");
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        await CreateApprovedDesignAsync(admin, productId, new[] { new { materialId = cement, quantityPerCft = 10m } });
        return new Setup(productId, plantId, cement);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;

    [Fact]
    public async Task A_cost_only_counts_in_the_floor_price_after_management_approves_it()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var setup = await SetupAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/material-costs",
            new { materialId = setup.Cement, costPerUnit = 8m, effectiveFrom = "2026-01-01" });
        var cost = await ReadAsync<MaterialCostDto>(created);
        Assert.Equal(CostStatus.Draft, cost.Status);

        // A draft is not used.
        Assert.Equal(HttpStatusCode.Conflict, (await FloorAsync(admin, setup.ProductId, setup.PlantId)).StatusCode);

        // Neither is one waiting for approval.
        (await admin.PostAsync($"/api/material-costs/{cost.Id}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await FloorAsync(admin, setup.ProductId, setup.PlantId)).StatusCode);

        (await admin.PostAsync($"/api/material-costs/{cost.Id}/approve", null)).EnsureSuccessStatusCode();
        var floor = await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId));
        Assert.Equal(80m, floor.MaterialCost);

        // A later cost does not take over until it is approved too.
        var next = await ReadAsync<MaterialCostDto>(await admin.PostAsJsonAsync("/api/material-costs",
            new { materialId = setup.Cement, costPerUnit = 9m, effectiveFrom = "2026-03-01" }));
        (await admin.PostAsync($"/api/material-costs/{next.Id}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(80m, (await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId))).MaterialCost);

        (await admin.PostAsync($"/api/material-costs/{next.Id}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(90m, (await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId))).MaterialCost);

        // Retiring the newer cost brings the older one back.
        (await admin.PostAsync($"/api/material-costs/{next.Id}/retire", null)).EnsureSuccessStatusCode();
        Assert.Equal(80m, (await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId))).MaterialCost);
    }

    [Fact]
    public async Task Overheads_and_delivery_bands_follow_the_same_approval()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var setup = await SetupAsync(admin);
        await CreateApprovedMaterialCostAsync(admin, setup.Cement, 8m);

        var overhead = await ReadAsync<OverheadCostDto>(await admin.PostAsJsonAsync("/api/overhead-costs",
            new { kind = "Overhead", name = "Labour", amountPerCft = 10m, validFrom = "2026-01-01" }));
        var band = await ReadAsync<DeliveryChargeBandDto>(await admin.PostAsJsonAsync("/api/delivery-charges",
            new { fromKm = 15m, toKm = 30m, chargePerCft = 9m }));

        var before = await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId, km: 20m));
        Assert.Equal(0m, before.Overhead);
        Assert.Equal(0m, before.Delivery);

        foreach (var route in new[] { $"/api/overhead-costs/{overhead.Id}", $"/api/delivery-charges/{band.Id}" })
        {
            (await admin.PostAsync($"{route}/submit", null)).EnsureSuccessStatusCode();
            (await admin.PostAsync($"{route}/approve", null)).EnsureSuccessStatusCode();
        }

        var after = await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId, km: 20m));
        Assert.Equal(10m, after.Overhead);
        Assert.Equal(9m, after.Delivery);

        // An approved row is never edited or deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/overhead-costs/{overhead.Id}",
            new { kind = "Overhead", name = "Labour", amountPerCft = 11m, validFrom = "2026-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/delivery-charges/{band.Id}")).StatusCode);

        // Retired, it stops counting.
        (await admin.PostAsync($"/api/overhead-costs/{overhead.Id}/retire", null)).EnsureSuccessStatusCode();
        Assert.Equal(0m, (await ReadAsync<FloorPriceBreakdown>(await FloorAsync(admin, setup.ProductId, setup.PlantId, km: 20m))).Overhead);
    }

    [Fact]
    public async Task A_band_that_overlaps_an_approved_one_waits_until_the_old_band_is_retired()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;

        async Task<DeliveryChargeBandDto> DraftAsync(decimal from, decimal? to, decimal charge) =>
            await ReadAsync<DeliveryChargeBandDto>(await admin.PostAsJsonAsync("/api/delivery-charges",
                new { fromKm = from, toKm = to, chargePerCft = charge }));

        var old = await DraftAsync(15m, 30m, 9m);
        (await admin.PostAsync($"/api/delivery-charges/{old.Id}/submit", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/delivery-charges/{old.Id}/approve", null)).EnsureSuccessStatusCode();

        var replacement = await DraftAsync(15m, 30m, 11m);
        (await admin.PostAsync($"/api/delivery-charges/{replacement.Id}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/delivery-charges/{replacement.Id}/approve", null)).StatusCode);

        (await admin.PostAsync($"/api/delivery-charges/{old.Id}/retire", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/delivery-charges/{replacement.Id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Rejecting_sends_a_cost_back_to_draft_with_the_reason()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");

        var cost = await ReadAsync<MaterialCostDto>(await admin.PostAsJsonAsync("/api/material-costs",
            new { materialId = cement, costPerUnit = 8m, effectiveFrom = "2026-01-01" }));

        // Not submitted yet, so there is nothing to reject or approve.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/material-costs/{cost.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/material-costs/{cost.Id}/reject", new { reason = "Too low" })).StatusCode);

        (await admin.PostAsync($"/api/material-costs/{cost.Id}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/material-costs/{cost.Id}/reject", new { reason = "x" })).StatusCode);
        (await admin.PostAsJsonAsync($"/api/material-costs/{cost.Id}/reject", new { reason = "Supplier quote is 9" })).EnsureSuccessStatusCode();

        var sentBack = await admin.GetFromJsonAsync<MaterialCostDto>($"/api/material-costs/{cost.Id}", ApiFactory.Json);
        Assert.Equal(CostStatus.Draft, sentBack!.Status);
        Assert.Equal("Supplier quote is 9", sentBack.RejectionReason);

        // It can be fixed and submitted again, which clears the reason.
        (await admin.PutAsJsonAsync($"/api/material-costs/{cost.Id}", new { costPerUnit = 9m, effectiveFrom = "2026-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/material-costs/{cost.Id}/submit", null)).EnsureSuccessStatusCode();
        var again = await admin.GetFromJsonAsync<MaterialCostDto>($"/api/material-costs/{cost.Id}", ApiFactory.Json);
        Assert.Equal(CostStatus.PendingApproval, again!.Status);
        Assert.Null(again.RejectionReason);

        var pending = await admin.GetFromJsonAsync<PagedResult<MaterialCostDto>>("/api/material-costs?status=PendingApproval", ApiFactory.Json);
        Assert.Equal(cost.Id, Assert.Single(pending!.Items).Id);
    }

    [Fact]
    public async Task Entering_costs_and_approving_them_are_different_permissions()
    {
        var company = await factory.CreateCompanyAsync();
        var cement = await CreateMaterialAsync(company.Admin.Client, "CEM", "Cement", "Kg");

        var accounts = await factory.AddMemberAsync(company, "Accounts");
        var management = await factory.AddMemberAsync(company, "Management");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var cost = await ReadAsync<MaterialCostDto>(await accounts.Client.PostAsJsonAsync("/api/material-costs",
            new { materialId = cement, costPerUnit = 8m, effectiveFrom = "2026-01-01" }));
        (await accounts.Client.PostAsync($"/api/material-costs/{cost.Id}/submit", null)).EnsureSuccessStatusCode();

        // The person who enters costs cannot approve them.
        Assert.Equal(HttpStatusCode.Forbidden, (await accounts.Client.PostAsync($"/api/material-costs/{cost.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accounts.Client.PostAsJsonAsync($"/api/material-costs/{cost.Id}/reject", new { reason = "No" })).StatusCode);

        // Management can see what is waiting and decide, but cannot enter costs.
        var waiting = await management.Client.GetFromJsonAsync<PagedResult<MaterialCostDto>>("/api/material-costs?status=PendingApproval", ApiFactory.Json);
        Assert.Equal(cost.Id, Assert.Single(waiting!.Items).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await management.Client.PostAsJsonAsync("/api/material-costs",
            new { materialId = cement, costPerUnit = 9m, effectiveFrom = "2026-02-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await management.Client.PostAsync($"/api/material-costs/{cost.Id}/approve", null)).StatusCode);

        // People with neither permission see nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.GetAsync("/api/material-costs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.PostAsync($"/api/material-costs/{cost.Id}/retire", null)).StatusCode);
    }
}
