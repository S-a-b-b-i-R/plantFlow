using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class OrderTests(ApiFactory factory)
{
    private record World(Guid ProductId, Guid PlantId, Guid CustomerId, Guid SiteId);

    // Cement costs 8 per kg, 10 kg per CFT, plus overhead 10: total cost 90. Default margin 10% gives a floor of 100.
    // The general price is 120.
    private static async Task<World> WorldAsync(HttpClient admin, string productCode = "G4000")
    {
        var cement = await CreateMaterialAsync(admin, "CEM", "Cement", "Kg");
        var plantId = await CreatePlantAsync(admin, "P1");
        var customerId = await CreateCustomerAsync(admin, "C1");
        var siteId = await CreateSiteAsync(admin, customerId, "Tower A");

        (await admin.PostAsJsonAsync("/api/material-costs",
            new { materialId = cement, costPerUnit = 8m, effectiveFrom = "2026-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/overhead-costs",
            new { kind = "Overhead", name = "Labour", amountPerCft = 10m, validFrom = "2026-01-01" })).EnsureSuccessStatusCode();

        var productId = await CreateProductAsync(admin, productCode);
        await CreateApprovedDesignAsync(admin, productId, new[] { new { materialId = cement, quantityPerCft = 10m } });
        await CreateActivePriceAsync(admin, productId, 120m);
        return new World(productId, plantId, customerId, siteId);
    }

    private static object Line(Guid productId, decimal qty, decimal? unitPrice = null, string element = "Slab", string method = "Chute") =>
        new { productId, quantityCft = qty, unitPrice, pourElement = element, pourMethod = method };

    private static Task<HttpResponseMessage> PostOrderAsync(
        HttpClient client, World w, object[] lines, decimal? distanceKm = 5m, string date = "2026-06-01", Guid? siteId = null) =>
        client.PostAsJsonAsync("/api/orders", new
        {
            customerId = w.CustomerId,
            siteId = siteId ?? w.SiteId,
            plantId = w.PlantId,
            requestedDate = date,
            distanceKm,
            lines
        });

    private static async Task<OrderDetailDto> CreateOrderAsync(
        HttpClient client, World w, object[] lines, decimal? distanceKm = 5m)
    {
        var response = await PostOrderAsync(client, w, lines, distanceKm);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!;
    }

    private static Task<OrderDetailDto?> GetOrderAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", ApiFactory.Json);

    [Fact]
    public async Task A_new_order_uses_the_active_price_and_calculates_the_floor()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);

        var order = await CreateOrderAsync(admin, w, [Line(w.ProductId, 100m)]);

        Assert.Equal("SO-000001", order.Order.Number);
        Assert.Equal(OrderStatus.Draft, order.Order.Status);
        Assert.Equal(5m, order.Order.DistanceKm);

        var line = Assert.Single(order.Lines);
        Assert.Equal(120m, line.UnitPrice);
        Assert.Equal(120m, line.ListPrice);
        Assert.NotNull(line.PriceId);
        Assert.Equal(100m, line.FloorPrice);
        Assert.NotNull(line.MixDesignId);
        Assert.False(line.BelowFloor);
        Assert.Equal(12000m, line.LineValue);

        Assert.Equal(100m, order.Order.TotalQuantityCft);
        Assert.Equal(12000m, order.Order.TotalValue);
        Assert.Equal(0, order.Order.BelowFloorLineCount);
        Assert.Equal(OrderStatus.Draft, Assert.Single(order.History).ToStatus);

        var second = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)]);
        Assert.Equal("SO-000002", second.Order.Number);
    }

    [Fact]
    public async Task A_price_below_the_floor_is_saved_and_flagged()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);

        // Floor is 100. 95 is below it, 100 is exactly on it, 150 is a raise above the list price.
        var order = await CreateOrderAsync(admin, w, [
            Line(w.ProductId, 10m, 95m, "Slab"),
            Line(w.ProductId, 20m, 100m, "Column"),
            Line(w.ProductId, 30m, 150m, "Footing")]);

        Assert.Equal(3, order.Lines.Count);
        Assert.Equal(1, order.Order.BelowFloorLineCount);
        Assert.Equal(60m, order.Order.TotalQuantityCft);
        Assert.Equal(10m * 95m + 20m * 100m + 30m * 150m, order.Order.TotalValue);

        Assert.True(order.Lines.Single(l => l.PourElement == PourElement.Slab).BelowFloor);
        Assert.False(order.Lines.Single(l => l.PourElement == PourElement.Column).BelowFloor);
        Assert.False(order.Lines.Single(l => l.PourElement == PourElement.Footing).BelowFloor);

        // The list price stays on record even when the rep entered another price.
        Assert.All(order.Lines, l => Assert.Equal(120m, l.ListPrice));
    }

    [Fact]
    public async Task The_distance_comes_from_the_request_or_the_site_and_changes_the_floor()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);
        var siteWithoutDistance = await CreateSiteAsync(admin, w.CustomerId, "Tower B");

        // Free up to 15 km, then 9 per CFT up to 30 km.
        (await admin.PostAsJsonAsync("/api/delivery-charges",
            new { fromKm = 15m, toKm = 30m, chargePerCft = 9m })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/sites/{w.SiteId}/distances",
            new { distances = new[] { new { plantId = w.PlantId, distanceKm = 20m } } })).EnsureSuccessStatusCode();

        // Nothing sent: the stored 20 km is used. Cost 99, floor 99 / 0.9 = 110.
        var fromSite = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)], distanceKm: null);
        Assert.Equal(20m, fromSite.Order.DistanceKm);
        Assert.Equal(110m, fromSite.Lines.Single().FloorPrice);

        // The rep's own value wins. Cost 90, floor 100.
        var overridden = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)], distanceKm: 10m);
        Assert.Equal(10m, overridden.Order.DistanceKm);
        Assert.Equal(100m, overridden.Lines.Single().FloorPrice);

        // No value and nothing stored for this site.
        var missing = await PostOrderAsync(admin, w, [Line(w.ProductId, 10m)], distanceKm: null, siteId: siteWithoutDistance);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task A_draft_can_be_edited_and_lines_are_updated_added_and_removed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);
        (await admin.PostAsJsonAsync("/api/delivery-charges",
            new { fromKm = 15m, toKm = 30m, chargePerCft = 9m })).EnsureSuccessStatusCode();

        var order = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m, element: "Slab"), Line(w.ProductId, 20m, element: "Column")]);
        var keep = order.Lines.Single(l => l.PourElement == PourElement.Slab);

        var edit = new
        {
            customerId = w.CustomerId,
            siteId = w.SiteId,
            plantId = w.PlantId,
            requestedDate = "2026-06-02",
            distanceKm = 20m,
            paymentTerms = "30 days",
            lines = new object[]
            {
                new { id = keep.Id, productId = w.ProductId, quantityCft = 15m, pourElement = "Slab", pourMethod = "Pump" },
                new { productId = w.ProductId, quantityCft = 5m, pourElement = "Road", pourMethod = "Chute" }
            }
        };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/orders/{order.Order.Id}", edit)).StatusCode);

        var reloaded = (await GetOrderAsync(admin, order.Order.Id))!;
        Assert.Equal(new DateOnly(2026, 6, 2), reloaded.Order.RequestedDate);
        Assert.Equal("30 days", reloaded.Order.PaymentTerms);
        Assert.Equal(2, reloaded.Lines.Count);
        Assert.DoesNotContain(reloaded.Lines, l => l.PourElement == PourElement.Column);
        Assert.Equal(15m, reloaded.Lines.Single(l => l.Id == keep.Id).QuantityCft);
        Assert.Equal(PourMethod.Pump, reloaded.Lines.Single(l => l.Id == keep.Id).PourMethod);
        Assert.Contains(reloaded.Lines, l => l.PourElement == PourElement.Road);

        // The new distance (20 km) puts the delivery band into the cost: floor 110 on every line.
        Assert.All(reloaded.Lines, l => Assert.Equal(110m, l.FloorPrice));

        (await admin.PostAsync($"/api/orders/{order.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        var afterSubmit = await admin.PutAsJsonAsync($"/api/orders/{order.Order.Id}", edit);
        Assert.Equal(HttpStatusCode.Conflict, afterSubmit.StatusCode);
    }

    [Fact]
    public async Task Submitting_needs_a_floor_price_for_every_line_and_records_history()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);

        // A product with a price but no approved recipe: the order saves, the floor is empty.
        var noRecipe = await CreateProductAsync(admin, "NORECIPE");
        await CreateActivePriceAsync(admin, noRecipe, 130m);

        var blocked = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m), Line(noRecipe, 10m)]);
        Assert.Null(blocked.Lines.Single(l => l.ProductCode == "NORECIPE").FloorPrice);

        var refused = await admin.PostAsync($"/api/orders/{blocked.Order.Id}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("NORECIPE", await refused.Content.ReadAsStringAsync());
        Assert.Equal(OrderStatus.Draft, (await GetOrderAsync(admin, blocked.Order.Id))!.Order.Status);

        var good = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)]);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{good.Order.Id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/orders/{good.Order.Id}/submit", null)).StatusCode);

        var submitted = (await GetOrderAsync(admin, good.Order.Id))!;
        Assert.Equal(OrderStatus.Submitted, submitted.Order.Status);
        Assert.Equal([OrderStatus.Draft, OrderStatus.Submitted], submitted.History.Select(h => h.ToStatus));
    }

    [Fact]
    public async Task An_order_can_be_cancelled_with_a_reason_and_then_cannot_move_on()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);
        var order = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)]);

        var noReason = await admin.PostAsJsonAsync($"/api/orders/{order.Order.Id}/cancel", new { reason = "" });
        var cancel = await admin.PostAsJsonAsync($"/api/orders/{order.Order.Id}/cancel", new { reason = "Customer postponed the pour" });
        var cancelAgain = await admin.PostAsJsonAsync($"/api/orders/{order.Order.Id}/cancel", new { reason = "Second try" });
        var submit = await admin.PostAsync($"/api/orders/{order.Order.Id}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);

        var cancelled = (await GetOrderAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.Equal("Customer postponed the pour", cancelled.Order.CancelReason);
        Assert.Equal([OrderStatus.Draft, OrderStatus.Cancelled], cancelled.History.Select(h => h.ToStatus));
    }

    [Fact]
    public async Task Invalid_orders_are_rejected()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await WorldAsync(admin);

        var otherCustomer = await CreateCustomerAsync(admin, "C2");
        var otherCustomersSite = await CreateSiteAsync(admin, otherCustomer, "Other site");
        var inactiveProduct = await CreateProductAsync(admin, "OLD", "Inactive");
        var noPrice = await CreateProductAsync(admin, "NOPRICE");

        var wrongSite = await PostOrderAsync(admin, w, [Line(w.ProductId, 10m)], siteId: otherCustomersSite);
        var inactive = await PostOrderAsync(admin, w, [Line(inactiveProduct, 10m)]);
        var priceMissing = await PostOrderAsync(admin, w, [Line(noPrice, 10m)]);
        var zeroQuantity = await PostOrderAsync(admin, w, [Line(w.ProductId, 0m)]);
        var tooPrecise = await PostOrderAsync(admin, w, [Line(w.ProductId, 1.234m)]);
        var noLines = await PostOrderAsync(admin, w, []);
        var preciseDistance = await PostOrderAsync(admin, w, [Line(w.ProductId, 10m)], distanceKm: 5.123m);
        var unknownProduct = await PostOrderAsync(admin, w, [Line(Guid.NewGuid(), 10m)]);
        var missingDate = await admin.PostAsJsonAsync("/api/orders", new
        {
            customerId = w.CustomerId,
            siteId = w.SiteId,
            plantId = w.PlantId,
            distanceKm = 5m,
            lines = new[] { Line(w.ProductId, 10m) }
        });
        var lineIdOnCreate = await PostOrderAsync(admin, w, [new
        {
            id = Guid.NewGuid(), productId = w.ProductId, quantityCft = 10m, pourElement = "Slab", pourMethod = "Chute"
        }]);

        Assert.All(
            new[] { wrongSite, inactive, priceMissing, zeroQuantity, tooPrecise, noLines, preciseDistance, unknownProduct, missingDate, lineIdOnCreate },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task The_list_supports_paging_search_and_filters()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await WorldAsync(admin);

        var first = await CreateOrderAsync(admin, w, [Line(w.ProductId, 10m)]);
        await CreateOrderAsync(admin, w, [Line(w.ProductId, 20m)]);
        var third = await CreateOrderAsync(admin, w, [Line(w.ProductId, 30m)]);
        await admin.PostAsJsonAsync($"/api/orders/{first.Order.Id}/cancel", new { reason = "Not needed" });

        var firstPage = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?pageSize=2", ApiFactory.Json);
        var secondPage = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?pageSize=2&page=2", ApiFactory.Json);
        var cancelled = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?status=Cancelled", ApiFactory.Json);
        var byNumber = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?search=SO-000003", ApiFactory.Json);
        var byCustomer = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>($"/api/orders?customerId={w.CustomerId}", ApiFactory.Json);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal("SO-000003", firstPage.Items[0].Number);    // newest first
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal(first.Order.Id, Assert.Single(cancelled!.Items).Id);
        Assert.Equal(third.Order.Id, Assert.Single(byNumber!.Items).Id);
        Assert.Equal(3, byCustomer!.TotalCount);
    }

    [Fact]
    public async Task Permissions_apply_and_companies_cannot_see_each_others_orders()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var w = await WorldAsync(company.Admin.Client);
        var order = await CreateOrderAsync(company.Admin.Client, w, [Line(w.ProductId, 10m)]);

        var rep = await factory.AddMemberAsync(company, "Sales Representative"); // can view, create and edit orders
        var created = await PostOrderAsync(rep.Client, w, [Line(w.ProductId, 10m)]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Take the create permission away: they can still read.
        (await company.Admin.Client.PutAsJsonAsync($"/api/users/{rep.Id}/permissions",
            new { overrides = new[] { new { permissionKey = Permissions.OrdersCreate, isGranted = false } } }))
            .EnsureSuccessStatusCode();

        var createAfter = await PostOrderAsync(rep.Client, w, [Line(w.ProductId, 10m)]);
        var listAfter = await rep.Client.GetAsync("/api/orders");

        var seenByOther = await other.Admin.Client.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders", ApiFactory.Json);
        var getByOther = await other.Admin.Client.GetAsync($"/api/orders/{order.Order.Id}");
        var submitByOther = await other.Admin.Client.PostAsync($"/api/orders/{order.Order.Id}/submit", null);
        var foreignSite = await PostOrderAsync(other.Admin.Client, w, [Line(w.ProductId, 10m)]);

        Assert.Equal(HttpStatusCode.Forbidden, createAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listAfter.StatusCode);
        Assert.Empty(seenByOther!.Items);
        Assert.Equal(HttpStatusCode.NotFound, getByOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, submitByOther.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreignSite.StatusCode);
    }

    [Fact]
    public async Task Site_distances_can_be_set_replaced_and_are_checked()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var customerId = await CreateCustomerAsync(admin, "C1");
        var siteId = await CreateSiteAsync(admin, customerId);
        var p1 = await CreatePlantAsync(admin, "P1");
        var p2 = await CreatePlantAsync(admin, "P2");
        var foreignPlant = await CreatePlantAsync(other.Admin.Client, "PX");

        var set = await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances", new
        {
            distances = new[] { new { plantId = p1, distanceKm = 12.5m }, new { plantId = p2, distanceKm = 30m } }
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var both = await admin.GetFromJsonAsync<List<SiteDistanceDto>>($"/api/sites/{siteId}/distances", ApiFactory.Json);
        Assert.Equal(["P1", "P2"], both!.Select(d => d.PlantCode));
        Assert.Equal(12.5m, both![0].DistanceKm);

        // Sending a shorter list replaces the old one.
        (await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId = p2, distanceKm = 28m } } })).EnsureSuccessStatusCode();
        var one = await admin.GetFromJsonAsync<List<SiteDistanceDto>>($"/api/sites/{siteId}/distances", ApiFactory.Json);
        Assert.Equal(28m, Assert.Single(one!).DistanceKm);

        var duplicate = await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances", new
        {
            distances = new[] { new { plantId = p1, distanceKm = 1m }, new { plantId = p1, distanceKm = 2m } }
        });
        var negative = await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId = p1, distanceKm = -1m } } });
        var tooPrecise = await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId = p1, distanceKm = 1.234m } } });
        var foreign = await admin.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId = foreignPlant, distanceKm = 5m } } });
        var unknownSite = await admin.GetAsync($"/api/sites/{Guid.NewGuid()}/distances");
        var otherCompany = await other.Admin.Client.GetAsync($"/api/sites/{siteId}/distances");

        var rep = await factory.AddMemberAsync(company, "Sales Representative"); // masterdata.view only
        var readAsRep = await rep.Client.GetAsync($"/api/sites/{siteId}/distances");
        var writeAsRep = await rep.Client.PutAsJsonAsync($"/api/sites/{siteId}/distances",
            new { distances = new[] { new { plantId = p1, distanceKm = 5m } } });

        Assert.All(new[] { duplicate, negative, tooPrecise, foreign }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknownSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherCompany.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readAsRep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, writeAsRep.StatusCode);
    }
}