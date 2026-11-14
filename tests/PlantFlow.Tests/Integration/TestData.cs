using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;

namespace PlantFlow.Tests.Integration;

// Shared setup for integration tests. Each helper creates one thing through the real API and returns it.
public static class TestData
{
    public static async Task<Guid> CreateProductAsync(HttpClient client, string code = "G4000", string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/products",
            new { code, name = code, strengthClass = "4000 psi", maxAggregateMm = 20, defaultSlumpMm = 100, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(ApiFactory.Json))!.Id;
    }

    public static async Task<Guid> CreatePlantAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/plants",
            new { code, name = $"Plant {code}", timeZone = "Asia/Dhaka", operatingHoursPerDay = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlantDetailDto>(ApiFactory.Json))!.Plant.Id;
    }

    public static async Task<Guid> CreateCustomerAsync(HttpClient client, string code, string type = "Contractor")
    {
        var response = await client.PostAsJsonAsync("/api/customers",
            new { code, name = $"Customer {code}", type, phone = "01700000000" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(ApiFactory.Json))!.Id;
    }

    public static async Task<Guid> CreateMaterialAsync(
        HttpClient client, string code, string category, string unit, string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/materials", new { code, name = code, category, unit, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaterialDto>(ApiFactory.Json))!.Id;
    }

    // Creates a mix design, submits it and approves it. Items look like new[] { new { materialId, quantityPerCft } }.
    public static async Task<MixDesignDetailDto> CreateApprovedDesignAsync(
        HttpClient client, Guid productId, object items, Guid? plantId = null)
    {
        var created = await client.PostAsJsonAsync("/api/mix-designs", new { productId, plantId, items });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var design = (await created.Content.ReadFromJsonAsync<MixDesignDetailDto>(ApiFactory.Json))!;

        (await client.PostAsync($"/api/mix-designs/{design.Design.Id}/submit", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/mix-designs/{design.Design.Id}/approve", null)).EnsureSuccessStatusCode();
        return design;
    }

    // Creates a price, submits it and approves it.
    public static async Task<PriceDto> CreateActivePriceAsync(
        HttpClient client, Guid productId, decimal price, string from = "2026-01-01", string? to = "2026-12-31",
        Guid? plantId = null, Guid? customerId = null)
    {
        var created = await client.PostAsJsonAsync("/api/prices",
            new { productId, plantId, customerId, pricePerCft = price, validFrom = from, validTo = to });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<PriceDto>(ApiFactory.Json))!;

        (await client.PostAsync($"/api/prices/{dto.Id}/submit", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/prices/{dto.Id}/approve", null)).EnsureSuccessStatusCode();
        return dto;
    }

    public static async Task<Guid> CreateSiteAsync(HttpClient client, Guid customerId, string name = "Site 1")
    {
        var response = await client.PostAsJsonAsync($"/api/customers/{customerId}/sites", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SiteDto>(ApiFactory.Json))!.Id;
    }

    public record OrderWorld(Guid ProductId, Guid PlantId, Guid CustomerId, Guid SiteId);

    // Cement costs 8 per kg, 10 kg per CFT, plus overhead 10: total cost 90. Default margin 10% gives a floor of 100.
    // The general price is 120.
    public static async Task<OrderWorld> CreateOrderWorldAsync(HttpClient admin, string productCode = "G4000")
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
        return new OrderWorld(productId, plantId, customerId, siteId);
    }

    public static object LineBody(Guid productId, decimal qty, decimal? unitPrice = null, string element = "Slab", string method = "Chute") =>
        new { productId, quantityCft = qty, unitPrice, pourElement = element, pourMethod = method };

    public static object OrderBody(OrderWorld w, object[] lines, decimal? distanceKm = 5m, string date = "2026-06-01") =>
        new { customerId = w.CustomerId, siteId = w.SiteId, plantId = w.PlantId, requestedDate = date, distanceKm, lines };

    public static async Task<OrderDetailDto> CreateDraftAsync(HttpClient client, OrderWorld w, object[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/orders", OrderBody(w, lines));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!;
    }
}