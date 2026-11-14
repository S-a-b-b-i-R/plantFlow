using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class OrderChargeTests(ApiFactory factory)
{
    private static Task<OrderDetailDto?> GetAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", ApiFactory.Json);

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid id, string route, bool approve, string? comment = null) =>
        client.PostAsJsonAsync($"/api/orders/{id}/approvals/{route}/{(approve ? "approve" : "reject")}", new { comment });

    // Sets the pump rate per CFT, whether the company already has a pump charge or not.
    private static async Task SetPumpRateAsync(HttpClient admin, decimal rate)
    {
        var body = new { code = "PUMP", name = "Pump charge", kind = "Pump", basis = "PerCft", defaultAmount = rate };
        var created = await admin.PostAsJsonAsync("/api/service-charges", body);
        if (created.StatusCode == HttpStatusCode.Created) return;

        var list = await admin.GetFromJsonAsync<PagedResult<ServiceChargeDto>>("/api/service-charges?kind=Pump", ApiFactory.Json);
        var id = list!.Items.Single().Id;
        (await admin.PutAsJsonAsync($"/api/service-charges/{id}", body)).EnsureSuccessStatusCode();
    }

    private static async Task SetFreeAboveAsync(HttpClient admin, decimal? freeAbove)
    {
        var settings = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        (await admin.PutAsJsonAsync("/api/settings", new
        {
            settings.Name,
            settings.VolumeUnit,
            pumpFreeAboveCft = freeAbove,
            settings.RequireSurveyBeforePlanning,
            settings.PumpRestShifts,
            settings.RequireCreditApproval
        }, ApiFactory.Json)).EnsureSuccessStatusCode();
    }

    // Created, submitted, approved and confirmed. The list price of 120 is above the floor, so only the manager approves.
    private static async Task<OrderDetailDto> ConfirmedAsync(HttpClient admin, OrderWorld w, object[] lines)
    {
        var draft = await CreateDraftAsync(admin, w, lines);
        (await admin.PostAsync($"/api/orders/{draft.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        (await DecideAsync(admin, draft.Order.Id, "sales-manager", true)).EnsureSuccessStatusCode();
        var confirm = await admin.PostAsJsonAsync($"/api/orders/{draft.Order.Id}/confirm", new { method = "Phone" });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        return (await GetAsync(admin, draft.Order.Id))!;
    }

    private static Task<HttpResponseMessage> AmendAsync(
        HttpClient client, OrderWorld w, OrderDetailDto order, decimal qty, decimal price)
    {
        var line = order.Lines.Single();
        return client.PutAsJsonAsync($"/api/orders/{order.Order.Id}/amend", new
        {
            reason = "Customer changed the order",
            order = new
            {
                customerId = w.CustomerId,
                siteId = w.SiteId,
                plantId = w.PlantId,
                requestedDate = "2026-06-01",
                distanceKm = 5m,
                lines = new[]
                {
                    new { id = line.Id, productId = line.ProductId, quantityCft = qty, unitPrice = price,
                          pourElement = "Slab", pourMethod = "Pump" }
                }
            }
        });
    }

    [Fact]
    public async Task The_pump_charge_counts_only_pumped_lines_at_the_configured_rate()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        var order = await ConfirmedAsync(admin, w,
        [
            LineBody(w.ProductId, 60m, null, "Slab", "Pump"),
            LineBody(w.ProductId, 40m, null, "Column", "Chute")
        ]);

        var charge = Assert.Single(order.Charges);
        Assert.Equal("Pump", charge.Kind.ToString());
        Assert.Equal(60m, charge.QuantityCft);
        Assert.Equal(15m, charge.RatePerCft);
        Assert.Equal(900m, charge.Amount);
    }

    [Fact]
    public async Task The_pump_is_free_only_above_the_company_threshold()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        // Orders with no pumping have no charge at all.
        var noPump = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 50m)]);
        Assert.Empty(noPump.Charges);

        // No threshold set: pumping is always charged.
        var always = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 600m, null, "Slab", "Pump")]);
        Assert.Equal(9000m, Assert.Single(always.Charges).Amount);

        await SetFreeAboveAsync(admin, 500m);

        // Exactly at the threshold is still charged; above it is free.
        var atThreshold = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 500m, null, "Slab", "Pump")]);
        Assert.Equal(7500m, Assert.Single(atThreshold.Charges).Amount);

        var above = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 501m, null, "Slab", "Pump")]);
        Assert.Empty(above.Charges);
    }

    [Fact]
    public async Task The_rate_is_frozen_at_confirmation_and_an_amendment_recalculates_the_charge()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        var order = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")]);
        Assert.Equal(150m, Assert.Single(order.Charges).Amount);

        // A new rate does not touch the confirmed order.
        await SetPumpRateAsync(admin, 20m);
        Assert.Equal(150m, Assert.Single((await GetAsync(admin, order.Order.Id))!.Charges).Amount);

        // A smaller amendment needs no approval, and the charge follows the new quantity at the current rate.
        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, order, 8m, 120m)).StatusCode);
        var amended = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Confirmed, amended.Order.Status);
        var charge = Assert.Single(amended.Charges);
        Assert.Equal(8m, charge.QuantityCft);
        Assert.Equal(20m, charge.RatePerCft);
        Assert.Equal(160m, charge.Amount);
    }

    [Fact]
    public async Task A_rejected_amendment_removes_the_charges_with_the_confirmation()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        var order = await ConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")]);
        Assert.Single(order.Charges);

        // A price below the floor of 100 sends it to Management, who reject it.
        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, order, 10m, 95m)).StatusCode);
        (await DecideAsync(admin, order.Order.Id, "management", false, "Too low")).EnsureSuccessStatusCode();

        var draft = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Draft, draft.Order.Status);
        Assert.Empty(draft.Charges);
    }
}