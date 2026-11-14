using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class VatOptionsTests(ApiFactory factory)
{
    private static Task<InvoiceDto?> InvoiceAsync(HttpClient c, Guid id) =>
        c.GetFromJsonAsync<InvoiceDto>($"/api/orders/{id}/invoice", ApiFactory.Json);

    private static async Task SetVatOnConcreteAsync(HttpClient admin, bool value)
    {
        var settings = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        (await admin.PutAsJsonAsync("/api/settings", new
        {
            settings.Name,
            settings.VolumeUnit,
            settings.PumpFreeAboveCft,
            settings.RequireSurveyBeforePlanning,
            settings.PumpRestShifts,
            settings.RequireCreditApproval,
            vatOnConcrete = value
        }, ApiFactory.Json)).EnsureSuccessStatusCode();
    }

    // Reads the customer, flips the switch and sends it back.
    private static async Task SetVatExemptAsync(HttpClient admin, Guid customerId, bool exempt)
    {
        var node = (await admin.GetFromJsonAsync<JsonObject>($"/api/customers/{customerId}"))!;
        node["vatExempt"] = exempt;
        (await admin.PutAsJsonAsync($"/api/customers/{customerId}", node)).EnsureSuccessStatusCode();
    }

    // 10 CFT pumped and delivered: concrete 1200 at the list price, pump charge 150 at 15 per CFT.
    private static async Task<Guid> DeliveredOrderAsync(HttpClient admin, OrderWorld w)
    {
        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")]);
        var id = order.Order.Id;
        (await admin.PutAsJsonAsync($"/api/orders/{id}/deliveries",
            new { lines = new[] { new { lineId = order.Lines.Single().Id, deliveredCft = 10m } } })).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Everything_carries_VAT_unless_configured_otherwise()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        var settings = await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json);
        Assert.True(settings!.VatOnConcrete);

        // 1200 + 150 = 1350, VAT 15% of 1350 = 202.50.
        var invoice = (await InvoiceAsync(admin, await DeliveredOrderAsync(admin, w)))!;
        Assert.False(invoice.VatExempt);
        Assert.Equal(1350m, invoice.Subtotal);
        Assert.Equal(1350m, invoice.TaxableAmount);
        Assert.Equal(202.5m, invoice.VatAmount);
        Assert.Equal(1552.5m, invoice.Total);
        Assert.True(Assert.Single(invoice.Charges).VatApplies);
    }

    [Fact]
    public async Task The_company_can_leave_concrete_untaxed_and_tax_only_the_charges()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);
        await SetVatOnConcreteAsync(admin, false);

        // Only the 150 pump charge is taxed: 22.50.
        var invoice = (await InvoiceAsync(admin, await DeliveredOrderAsync(admin, w)))!;
        Assert.Equal(1350m, invoice.Subtotal);
        Assert.Equal(150m, invoice.TaxableAmount);
        Assert.Equal(22.5m, invoice.VatAmount);
        Assert.Equal(1372.5m, invoice.Total);
    }

    [Fact]
    public async Task A_charge_can_be_untaxed_and_adjustments_reduce_the_taxable_amount_but_not_below_zero()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m, vatApplies: false);

        var id = await DeliveredOrderAsync(admin, w);
        var plain = (await InvoiceAsync(admin, id))!;
        Assert.False(Assert.Single(plain.Charges).VatApplies);
        Assert.Equal(1200m, plain.TaxableAmount);
        Assert.Equal(180m, plain.VatAmount);
        Assert.Equal(1530m, plain.Total);

        // A 200 adjustment comes off the taxable 1200: 1000, VAT 150. Subtotal 1350 - 200 = 1150.
        (await admin.PostAsJsonAsync($"/api/orders/{id}/adjustments",
            new { kind = "Discount", reason = "Goodwill", amount = 200m })).EnsureSuccessStatusCode();
        var adjusted = (await InvoiceAsync(admin, id))!;
        Assert.Equal(1000m, adjusted.TaxableAmount);
        Assert.Equal(150m, adjusted.VatAmount);
        Assert.Equal(1150m, adjusted.Subtotal);
        Assert.Equal(1300m, adjusted.Total);

        // With concrete and the charge both untaxed, the adjustment cannot make the taxable amount negative.
        await SetVatOnConcreteAsync(admin, false);
        var other = await DeliveredOrderAsync(admin, w);
        (await admin.PostAsJsonAsync($"/api/orders/{other}/adjustments",
            new { kind = "Discount", reason = "Goodwill", amount = 200m })).EnsureSuccessStatusCode();
        var none = (await InvoiceAsync(admin, other))!;
        Assert.Equal(0m, none.TaxableAmount);
        Assert.Equal(0m, none.VatAmount);
        Assert.Equal(1150m, none.Total);
    }

    [Fact]
    public async Task An_exempt_customer_pays_no_VAT()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);
        await SetVatExemptAsync(admin, w.CustomerId, true);

        var invoice = (await InvoiceAsync(admin, await DeliveredOrderAsync(admin, w)))!;
        Assert.True(invoice.VatExempt);
        Assert.Equal(0m, invoice.TaxableAmount);
        Assert.Equal(0m, invoice.VatAmount);
        Assert.Equal(1350m, invoice.Total);
    }

    [Fact]
    public async Task The_terms_are_frozen_when_the_order_is_confirmed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        await SetPumpRateAsync(admin, 15m);

        var id = await DeliveredOrderAsync(admin, w);
        var before = (await InvoiceAsync(admin, id))!;
        Assert.Equal(202.5m, before.VatAmount);

        // Every option changes afterwards. The confirmed order does not.
        await SetVatOnConcreteAsync(admin, false);
        await SetPumpRateAsync(admin, 15m, vatApplies: false);
        await SetVatExemptAsync(admin, w.CustomerId, true);

        var after = (await InvoiceAsync(admin, id))!;
        Assert.False(after.VatExempt);
        Assert.Equal(1350m, after.TaxableAmount);
        Assert.Equal(202.5m, after.VatAmount);
        Assert.Equal(1552.5m, after.Total);
    }
}