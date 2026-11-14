using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class OrderBillingTests(ApiFactory factory)
{
    private static Task<OrderDetailDto?> GetAsync(HttpClient c, Guid id) =>
        c.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", ApiFactory.Json);

    private static Task<InvoiceDto?> InvoiceAsync(HttpClient c, Guid id) =>
        c.GetFromJsonAsync<InvoiceDto>($"/api/orders/{id}/invoice", ApiFactory.Json);

    private static Task<HttpResponseMessage> DeliverAsync(HttpClient c, Guid id, params (Guid LineId, decimal Qty)[] lines) =>
        c.PutAsJsonAsync($"/api/orders/{id}/deliveries",
            new { lines = lines.Select(l => new { lineId = l.LineId, deliveredCft = l.Qty }).ToArray() });

    private static Task<HttpResponseMessage> ResolveAsync(
        HttpClient c, Guid id, Guid lineId, string basis, string reason = "Agreed with the customer", decimal? billedCft = null) =>
        c.PutAsJsonAsync($"/api/orders/{id}/billing", new { lines = new[] { new { lineId, basis, billedCft, reason } } });

    private static Task<HttpResponseMessage> AdjustAsync(
        HttpClient c, Guid id, decimal amount, string reason = "Truck rejected at site", string kind = "QcReject") =>
        c.PostAsJsonAsync($"/api/orders/{id}/adjustments", new { kind, reason, amount });

    private static Task<HttpResponseMessage> CloseAsync(HttpClient c, Guid id) => c.PostAsync($"/api/orders/{id}/close", null);

    [Fact]
    public async Task Recording_a_delivery_starts_the_order_and_needs_its_permission()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var management = await factory.AddMemberAsync(company, "Management");

        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        Assert.Equal(HttpStatusCode.Conflict, (await DeliverAsync(admin, draft.Order.Id, (draft.Lines.Single().Id, 10m))).StatusCode);

        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        var id = order.Order.Id;
        var lineId = order.Lines.Single().Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await DeliverAsync(management.Client, id, (lineId, 10m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeliverAsync(other.Admin.Client, id, (lineId, 10m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeliverAsync(admin, id, (Guid.NewGuid(), 5m))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeliverAsync(admin, id, (lineId, 10m))).StatusCode);

        var running = (await GetAsync(admin, id))!;
        Assert.Equal(OrderStatus.InProgress, running.Order.Status);
        Assert.Equal(OrderStatus.InProgress, running.History.Last().ToStatus);

        // Delivered equals ordered, so nothing needs resolving. 1200 plus 15% VAT.
        var invoice = (await InvoiceAsync(admin, id))!;
        Assert.False(invoice.Final);
        Assert.False(invoice.Lines.Single().NeedsResolution);
        Assert.Equal(1200m, invoice.Subtotal);
        Assert.Equal(180m, invoice.VatAmount);
        Assert.Equal(1380m, invoice.Total);
        Assert.True(invoice.CanClose);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/orders/{id}/invoice")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CloseAsync(other.Admin.Client, id)).StatusCode);
    }

    [Fact]
    public async Task A_difference_must_be_resolved_before_the_order_can_close()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        var id = order.Order.Id;
        var lineId = order.Lines.Single().Id;
        (await DeliverAsync(admin, id, (lineId, 8m))).EnsureSuccessStatusCode();

        var open = (await InvoiceAsync(admin, id))!;
        Assert.True(open.Lines.Single().NeedsResolution);
        Assert.False(open.CanClose);
        Assert.Equal(HttpStatusCode.Conflict, (await CloseAsync(admin, id)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(admin, id, lineId, "Custom")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(admin, id, lineId, "Delivered", billedCft: 8m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(admin, id, lineId, "Delivered", reason: "")).StatusCode);

        (await ResolveAsync(admin, id, lineId, "Delivered")).EnsureSuccessStatusCode();
        var resolved = (await InvoiceAsync(admin, id))!;
        Assert.Equal(8m, resolved.Lines.Single().BilledCft);
        Assert.Equal(BillingBasis.Delivered, resolved.Lines.Single().Basis);
        Assert.False(resolved.Lines.Single().NeedsResolution);
        Assert.True(resolved.CanClose);

        // A different delivered quantity clears the resolution again.
        (await DeliverAsync(admin, id, (lineId, 9m))).EnsureSuccessStatusCode();
        Assert.True((await InvoiceAsync(admin, id))!.Lines.Single().NeedsResolution);
        (await ResolveAsync(admin, id, lineId, "Delivered")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(admin, id)).StatusCode);
        var closed = (await GetAsync(admin, id))!;
        Assert.Equal(OrderStatus.Completed, closed.Order.Status);

        // 9 CFT at 120 is 1080, plus 15% VAT of 162.
        var invoice = (await InvoiceAsync(admin, id))!;
        Assert.True(invoice.Final);
        Assert.Equal(1080m, invoice.Subtotal);
        Assert.Equal(162m, invoice.VatAmount);
        Assert.Equal(1242m, invoice.Total);

        // Closed means closed.
        Assert.Equal(HttpStatusCode.Conflict, (await DeliverAsync(admin, id, (lineId, 5m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ResolveAsync(admin, id, lineId, "Ordered")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AdjustAsync(admin, id, 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CloseAsync(admin, id)).StatusCode);
    }

    [Fact]
    public async Task Each_line_can_be_billed_on_a_different_basis()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var order = await CreateConfirmedAsync(admin, w,
        [
            LineBody(w.ProductId, 10m, null, "Slab"),
            LineBody(w.ProductId, 20m, null, "Column"),
            LineBody(w.ProductId, 5m, null, "Footing")
        ]);
        var id = order.Order.Id;
        var slab = order.Lines.Single(l => l.QuantityCft == 10m).Id;
        var column = order.Lines.Single(l => l.QuantityCft == 20m).Id;
        var footing = order.Lines.Single(l => l.QuantityCft == 5m).Id;

        // Slab over-delivered, column under-delivered, footing exact.
        (await DeliverAsync(admin, id, (slab, 12m), (column, 15m), (footing, 5m))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await CloseAsync(admin, id)).StatusCode);

        (await ResolveAsync(admin, id, slab, "Ordered", "The extra was a gift")).EnsureSuccessStatusCode();
        (await ResolveAsync(admin, id, column, "Custom", "Agreed after the site check", 18m)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(admin, id)).StatusCode);

        // 10 x 120 + 18 x 120 + 5 x 120 = 3960, VAT 594.
        var invoice = (await InvoiceAsync(admin, id))!;
        Assert.Equal(10m, invoice.Lines.Single(l => l.LineId == slab).BilledCft);
        Assert.Equal(18m, invoice.Lines.Single(l => l.LineId == column).BilledCft);
        Assert.Equal(5m, invoice.Lines.Single(l => l.LineId == footing).BilledCft);
        Assert.Equal(3960m, invoice.Subtotal);
        Assert.Equal(594m, invoice.VatAmount);
        Assert.Equal(4554m, invoice.Total);
    }

    [Fact]
    public async Task Charges_and_adjustments_change_the_invoice_total()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var management = await factory.AddMemberAsync(company, "Management");
        await SetPumpRateAsync(admin, 15m);

        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m, null, "Slab", "Pump")]);
        var id = order.Order.Id;
        Assert.Equal(150m, Assert.Single(order.Charges).Amount);
        (await DeliverAsync(admin, id, (order.Lines.Single().Id, 10m))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await AdjustAsync(admin, id, 0m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdjustAsync(management.Client, id, 200m)).StatusCode);

        var added = await AdjustAsync(admin, id, 200m);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var adjustment = (await added.Content.ReadFromJsonAsync<InvoiceAdjustmentDto>(ApiFactory.Json))!;

        // Concrete 1200 plus the pump charge 150, less 200, is 1150. VAT 172.50.
        var invoice = (await InvoiceAsync(admin, id))!;
        Assert.Equal(1200m, invoice.ConcreteAmount);
        Assert.Equal(150m, invoice.ChargesAmount);
        Assert.Equal(200m, invoice.AdjustmentsAmount);
        Assert.Equal(1150m, invoice.Subtotal);
        Assert.Equal(172.5m, invoice.VatAmount);
        Assert.Equal(1322.5m, invoice.Total);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/orders/{id}/adjustments/{adjustment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/orders/{id}/adjustments/{adjustment.Id}")).StatusCode);
        Assert.Equal(1350m, (await InvoiceAsync(admin, id))!.Subtotal);

        (await AdjustAsync(admin, id, 200m)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(admin, id)).StatusCode);

        var final = (await InvoiceAsync(admin, id))!;
        Assert.True(final.Final);
        Assert.Equal(1322.5m, final.Total);
    }
}