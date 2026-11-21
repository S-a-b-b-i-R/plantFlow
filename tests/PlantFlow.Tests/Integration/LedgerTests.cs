using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Ledger;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class LedgerTests(ApiFactory factory)
{
    private static Task<InvoiceDto?> InvoiceAsync(HttpClient c, Guid id) =>
        c.GetFromJsonAsync<InvoiceDto>($"/api/orders/{id}/invoice", ApiFactory.Json);

    private static Task<LedgerStatementDto?> StatementAsync(HttpClient c, Guid customerId) =>
        c.GetFromJsonAsync<LedgerStatementDto>($"/api/customers/{customerId}/ledger", ApiFactory.Json);

    private static Task<HttpResponseMessage> DeliverAsync(HttpClient c, Guid id, Guid lineId, decimal qty) =>
        c.PutAsJsonAsync($"/api/orders/{id}/deliveries", new { lines = new[] { new { lineId, deliveredCft = qty } } });

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient c, Guid id, Guid lineId, string basis) =>
        c.PutAsJsonAsync($"/api/orders/{id}/billing",
            new { lines = new[] { new { lineId, basis, billedCft = (decimal?)null, reason = "Site finished early" } } });

    private static Task<HttpResponseMessage> UseAsync(HttpClient c, Guid orderId, Guid lineId, Guid creditId, decimal qty) =>
        c.PostAsJsonAsync($"/api/orders/{orderId}/credit-uses", new { lineId, creditId, quantityCft = qty });

    private static Task<HttpResponseMessage> AddCreditAsync(
        HttpClient c, Guid customerId, Guid productId, decimal qty, decimal price = 100m, string reason = "Opening balance") =>
        c.PostAsJsonAsync($"/api/customers/{customerId}/ledger/credits",
            new { productId, quantityCft = qty, unitPrice = price, reason });

    private static Task<HttpResponseMessage> RequestCloseAsync(
        HttpClient c, Guid creditId, decimal? qty = null, string reason = "Refunded", string note = "Paid back by cheque") =>
        c.PostAsJsonAsync($"/api/ledger/credits/{creditId}/closes", new { quantityCft = qty, reason, note });

    private static async Task<LedgerCreditDto> CreditAsync(HttpResponseMessage created)
    {
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<LedgerCreditDto>(ApiFactory.Json))!;
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemTitle>())!.Title;

    private sealed record ProblemTitle(string Title);

    // A confirmed order of 10 CFT at 120, delivered 8, carried forward and closed: 2 CFT at 120 in the ledger.
    private static async Task<(OrderDetailDto Order, LedgerCreditDto Credit)> CarryForwardAsync(HttpClient admin, OrderWorld w)
    {
        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        var lineId = order.Lines.Single().Id;
        (await DeliverAsync(admin, order.Order.Id, lineId, 8m)).EnsureSuccessStatusCode();
        (await ResolveAsync(admin, order.Order.Id, lineId, "CarriedForward")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{order.Order.Id}/close", null)).StatusCode);

        var statement = (await StatementAsync(admin, w.CustomerId))!;
        return (order, statement.Credits.Single(c => c.SourceOrderId == order.Order.Id));
    }

    // A running order of 10 CFT at the given price, delivered in full.
    private static async Task<OrderDetailDto> RunningAsync(HttpClient admin, OrderWorld w, decimal price = 150m)
    {
        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m, price)]);
        (await DeliverAsync(admin, order.Order.Id, order.Lines.Single().Id, 10m)).EnsureSuccessStatusCode();
        return order;
    }

    [Fact]
    public async Task Carrying_forward_bills_what_was_delivered_and_credits_the_rest_at_the_order_price()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var order = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        var id = order.Order.Id;
        var lineId = order.Lines.Single().Id;

        // Delivered in full: nothing to carry forward.
        (await DeliverAsync(admin, id, lineId, 10m)).EnsureSuccessStatusCode();
        var full = await ResolveAsync(admin, id, lineId, "CarriedForward");
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("Only a line delivered short of what was ordered can carry the rest forward.", await TitleAsync(full));

        (await DeliverAsync(admin, id, lineId, 8m)).EnsureSuccessStatusCode();
        (await ResolveAsync(admin, id, lineId, "CarriedForward")).EnsureSuccessStatusCode();

        var invoice = (await InvoiceAsync(admin, id))!;
        var line = invoice.Lines.Single();
        Assert.Equal(8m, line.BilledCft);
        Assert.Equal(2m, line.CarriedForwardCft);
        Assert.Equal(960m, line.Amount);

        // Nothing goes to the ledger until the order closes.
        Assert.Empty((await StatementAsync(admin, w.CustomerId))!.Credits);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{id}/close", null)).StatusCode);

        var statement = (await StatementAsync(admin, w.CustomerId))!;
        var credit = Assert.Single(statement.Credits);
        Assert.Equal(LedgerCreditSource.CarriedForward, credit.Source);
        Assert.Equal(order.Order.Number, credit.SourceOrderNumber);
        Assert.Equal(2m, credit.OriginalCft);
        Assert.Equal(2m, credit.BalanceCft);
        Assert.Equal(120m, credit.UnitPrice);
        Assert.Equal(2m, statement.OpenCft);
        Assert.Equal(240m, statement.OpenValue);

        var row = Assert.Single(statement.Orders.Items);
        Assert.True(row.Final);
        Assert.Equal(10m, row.OrderedCft);
        Assert.Equal(8m, row.DeliveredCft);
        Assert.Equal(8m, row.BilledCft);
        Assert.Equal(2m, row.CarriedForwardCft);

        var listed = (await admin.GetFromJsonAsync<PagedResult<LedgerCustomerDto>>("/api/ledger", ApiFactory.Json))!;
        Assert.Equal(2m, Assert.Single(listed.Items).OpenCft);
    }

    [Fact]
    public async Task A_credit_is_billed_at_its_locked_price_on_a_later_order()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var (_, credit) = await CarryForwardAsync(admin, w);

        var later = await RunningAsync(admin, w, 150m);
        var id = later.Order.Id;
        var lineId = later.Lines.Single().Id;

        var available = (await admin.GetFromJsonAsync<List<AvailableCreditDto>>($"/api/orders/{id}/ledger-credits", ApiFactory.Json))!;
        Assert.Equal(credit.Id, Assert.Single(available).CreditId);

        var tooMuch = await UseAsync(admin, id, lineId, credit.Id, 3m);
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal("Only 2 CFT of that credit is left to use.", await TitleAsync(tooMuch));

        Assert.Equal(HttpStatusCode.NoContent, (await UseAsync(admin, id, lineId, credit.Id, 2m)).StatusCode);

        // 2 CFT at 120 and 8 CFT at 150.
        var invoice = (await InvoiceAsync(admin, id))!;
        var line = invoice.Lines.Single();
        Assert.Equal(1440m, line.Amount);
        var use = Assert.Single(line.CreditUses);
        Assert.Equal(120m, use.UnitPrice);
        Assert.Equal(240m, use.Amount);
        Assert.Equal(0m, (await StatementAsync(admin, w.CustomerId))!.Credits.Single().BalanceCft);
        Assert.Empty((await admin.GetFromJsonAsync<List<AvailableCreditDto>>($"/api/orders/{id}/ledger-credits", ApiFactory.Json))!);

        // Removing the use gives the volume back; using it again and closing makes it final.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/orders/{id}/credit-uses/{use.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/orders/{id}/credit-uses/{use.Id}")).StatusCode);
        Assert.Equal(2m, (await StatementAsync(admin, w.CustomerId))!.Credits.Single().BalanceCft);
        Assert.Equal(1500m, (await InvoiceAsync(admin, id))!.Lines.Single().Amount);

        (await UseAsync(admin, id, lineId, credit.Id, 2m)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{id}/close", null)).StatusCode);
        Assert.Equal(1440m, (await InvoiceAsync(admin, id))!.Subtotal);

        var closedUse = (await InvoiceAsync(admin, id))!.Lines.Single().CreditUses.Single();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/orders/{id}/credit-uses/{closedUse.Id}")).StatusCode);

        var history = (await StatementAsync(admin, w.CustomerId))!.Credits.Single().Entries;
        Assert.Equal([LedgerEntryKind.Created, LedgerEntryKind.Used], history.Select(e => e.Kind));
        Assert.Equal(-2m, history[1].QuantityCft);
        Assert.Equal(later.Order.Number, history[1].OrderNumber);
    }

    [Fact]
    public async Task A_credit_is_refused_on_another_product_another_customer_or_beyond_what_is_billed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var otherProduct = await CreateProductAsync(admin, "G5000");
        var otherCustomer = await CreateCustomerAsync(admin, "C2");

        var confirmed = await CreateConfirmedAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        var big = await CreditAsync(await AddCreditAsync(admin, w.CustomerId, w.ProductId, 20m));

        var notRunning = await UseAsync(admin, confirmed.Order.Id, confirmed.Lines.Single().Id, big.Id, 1m);
        Assert.Equal(HttpStatusCode.Conflict, notRunning.StatusCode);
        Assert.Equal("Credit can only be used on a running order. This order is Confirmed.", await TitleAsync(notRunning));

        var order = await RunningAsync(admin, w);
        var id = order.Order.Id;
        var lineId = order.Lines.Single().Id;

        var wrongProduct = await CreditAsync(await AddCreditAsync(admin, w.CustomerId, otherProduct, 5m));
        var product = await UseAsync(admin, id, lineId, wrongProduct.Id, 1m);
        Assert.Equal(HttpStatusCode.Conflict, product.StatusCode);
        Assert.Equal("A credit can only be used on the same product it was carried forward for.", await TitleAsync(product));

        var theirs = await CreditAsync(await AddCreditAsync(admin, otherCustomer, w.ProductId, 5m));
        var customer = await UseAsync(admin, id, lineId, theirs.Id, 1m);
        Assert.Equal(HttpStatusCode.Conflict, customer.StatusCode);
        Assert.Equal("That credit belongs to another customer.", await TitleAsync(customer));

        var beyond = await UseAsync(admin, id, lineId, big.Id, 11m);
        Assert.Equal(HttpStatusCode.Conflict, beyond.StatusCode);
        Assert.Equal("This line is billed for 10 CFT and 0 CFT of it already comes from credits. Use less credit.",
            await TitleAsync(beyond));

        Assert.Equal(HttpStatusCode.BadRequest, (await UseAsync(admin, id, Guid.NewGuid(), big.Id, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UseAsync(admin, id, lineId, big.Id, 1.001m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UseAsync(admin, id, lineId, Guid.NewGuid(), 1m)).StatusCode);

        // Deliveries that drop below the credit used block the close until some credit is removed.
        (await UseAsync(admin, id, lineId, big.Id, 8m)).EnsureSuccessStatusCode();
        (await DeliverAsync(admin, id, lineId, 5m)).EnsureSuccessStatusCode();
        (await ResolveAsync(admin, id, lineId, "Delivered")).EnsureSuccessStatusCode();

        var invoice = (await InvoiceAsync(admin, id))!;
        Assert.True(invoice.Lines.Single().CreditOverBilled);
        Assert.False(invoice.CanClose);
        var close = await admin.PostAsync($"/api/orders/{id}/close", null);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        Assert.Equal("More credit is used than these lines are billed for: G4000. Remove some of the credit first.",
            await TitleAsync(close));
    }

    [Fact]
    public async Task Cancelling_an_order_gives_its_credit_back()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var credit = await CreditAsync(await AddCreditAsync(admin, w.CustomerId, w.ProductId, 5m));

        var order = await RunningAsync(admin, w);
        var id = order.Order.Id;
        (await UseAsync(admin, id, order.Lines.Single().Id, credit.Id, 3m)).EnsureSuccessStatusCode();
        Assert.Equal(2m, (await StatementAsync(admin, w.CustomerId))!.Credits.Single().BalanceCft);

        (await admin.PostAsJsonAsync($"/api/orders/{id}/hold", new { reason = "Customer stopped the job" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{id}/cancel", new { reason = "Job abandoned" })).EnsureSuccessStatusCode();

        var after = (await StatementAsync(admin, w.CustomerId))!.Credits.Single();
        Assert.Equal(5m, after.BalanceCft);
        Assert.DoesNotContain(after.Entries, e => e.Kind == LedgerEntryKind.Used);
    }

    [Fact]
    public async Task Accounts_adds_and_corrects_credits_with_a_reason()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var accounts = await factory.AddMemberAsync(company, "Accounts");
        var salesManager = await factory.AddMemberAsync(company, "Sales Manager");

        Assert.Equal(HttpStatusCode.Forbidden, (await AddCreditAsync(salesManager.Client, w.CustomerId, w.ProductId, 5m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddCreditAsync(accounts.Client, w.CustomerId, w.ProductId, 5m, reason: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddCreditAsync(accounts.Client, w.CustomerId, w.ProductId, 5.001m)).StatusCode);
        var noProduct = await AddCreditAsync(accounts.Client, w.CustomerId, Guid.NewGuid(), 5m);
        Assert.Equal(HttpStatusCode.BadRequest, noProduct.StatusCode);
        Assert.Equal("Choose a product from the catalogue.", await TitleAsync(noProduct));
        Assert.Equal(HttpStatusCode.NotFound, (await AddCreditAsync(accounts.Client, Guid.NewGuid(), w.ProductId, 5m)).StatusCode);

        var credit = await CreditAsync(await AddCreditAsync(accounts.Client, w.CustomerId, w.ProductId, 5m, 110m));
        Assert.Equal(LedgerCreditSource.Manual, credit.Source);
        Assert.Equal("Opening balance", credit.Note);

        var url = $"/api/ledger/credits/{credit.Id}/corrections";
        Assert.Equal(HttpStatusCode.Forbidden,
            (await salesManager.Client.PostAsJsonAsync(url, new { quantityCft = 1m, reason = "Recount" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await accounts.Client.PostAsJsonAsync(url, new { quantityCft = 0m, reason = "Recount" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await accounts.Client.PostAsJsonAsync(url, new { quantityCft = 1m, reason = "" })).StatusCode);

        var below = await accounts.Client.PostAsJsonAsync(url, new { quantityCft = -6m, reason = "Recount" });
        Assert.Equal(HttpStatusCode.Conflict, below.StatusCode);
        Assert.Equal("Only 5 CFT is left on this credit to take away.", await TitleAsync(below));

        Assert.Equal(HttpStatusCode.NoContent,
            (await accounts.Client.PostAsJsonAsync(url, new { quantityCft = -1.5m, reason = "Recount" })).StatusCode);

        var after = (await StatementAsync(accounts.Client, w.CustomerId))!.Credits.Single();
        Assert.Equal(3.5m, after.BalanceCft);
        Assert.Equal(-1.5m, after.Entries.Single(e => e.Kind == LedgerEntryKind.Corrected).QuantityCft);

        var actions = (await AuditAsync(admin)).Select(a => a.Action).ToList();
        Assert.Contains("ledger.credit_added", actions);
        Assert.Contains("ledger.credit_corrected", actions);
    }

    [Fact]
    public async Task Closing_a_credit_needs_someone_else_with_ledger_approve()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var accounts = await factory.AddMemberAsync(company, "Accounts");
        var salesManager = await factory.AddMemberAsync(company, "Sales Manager");
        var credit = await CreditAsync(await AddCreditAsync(accounts.Client, w.CustomerId, w.ProductId, 5m));

        Assert.Equal(HttpStatusCode.Forbidden, (await RequestCloseAsync(salesManager.Client, credit.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestCloseAsync(accounts.Client, credit.Id, note: "")).StatusCode);
        var tooMuch = await RequestCloseAsync(accounts.Client, credit.Id, 6m);
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal("Only 5 CFT is left on this credit to close.", await TitleAsync(tooMuch));

        Assert.Equal(HttpStatusCode.NoContent, (await RequestCloseAsync(accounts.Client, credit.Id)).StatusCode);
        var again = await RequestCloseAsync(accounts.Client, credit.Id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This credit already has a close waiting for approval.", await TitleAsync(again));

        // The volume is held while the close waits.
        var held = (await StatementAsync(accounts.Client, w.CustomerId))!.Credits.Single();
        Assert.Equal(0m, held.BalanceCft);
        Assert.Equal(5m, held.PendingCloseCft);
        var order = await RunningAsync(admin, w);
        Assert.Equal(HttpStatusCode.Conflict, (await UseAsync(admin, order.Order.Id, order.Lines.Single().Id, credit.Id, 1m)).StatusCode);

        var inbox = (await salesManager.Client.GetFromJsonAsync<List<LedgerCloseDto>>("/api/ledger/closes?status=Pending", ApiFactory.Json))!;
        var close = Assert.Single(inbox);
        Assert.False(close.RequestedByMe);
        Assert.Equal(LedgerCloseReason.Refunded, close.Reason);
        Assert.Equal(500m, close.Value);

        Assert.Equal(HttpStatusCode.Forbidden, (await accounts.Client.PostAsync($"/api/ledger/closes/{close.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await salesManager.Client.PostAsync($"/api/ledger/closes/{close.Id}/approve", null)).StatusCode);

        var decided = await salesManager.Client.PostAsync($"/api/ledger/closes/{close.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, decided.StatusCode);
        Assert.Equal("This close has already been decided.", await TitleAsync(decided));

        var closed = (await StatementAsync(accounts.Client, w.CustomerId))!.Credits.Single();
        Assert.Equal(0m, closed.BalanceCft);
        Assert.Equal(0m, closed.PendingCloseCft);
        var entry = closed.Entries.Single(e => e.Kind == LedgerEntryKind.Closed);
        Assert.Equal(LedgerCloseStatus.Approved, entry.CloseStatus);
        Assert.Equal(-5m, entry.QuantityCft);

        // Used up and closed: off the default list, still on ?all=true.
        Assert.Empty((await admin.GetFromJsonAsync<PagedResult<LedgerCustomerDto>>("/api/ledger", ApiFactory.Json))!.Items);
        Assert.Single((await admin.GetFromJsonAsync<PagedResult<LedgerCustomerDto>>("/api/ledger?all=true", ApiFactory.Json))!.Items);
    }

    [Fact]
    public async Task A_close_sent_back_puts_the_volume_back_and_nobody_decides_their_own()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var salesManager = await factory.AddMemberAsync(company, "Sales Manager");
        var credit = await CreditAsync(await AddCreditAsync(admin, w.CustomerId, w.ProductId, 5m));

        (await RequestCloseAsync(admin, credit.Id, 2m, "NotNeeded", "Job finished")).EnsureSuccessStatusCode();
        var close = (await admin.GetFromJsonAsync<List<LedgerCloseDto>>("/api/ledger/closes?status=Pending", ApiFactory.Json))!.Single();
        Assert.True(close.RequestedByMe);

        var own = await admin.PostAsync($"/api/ledger/closes/{close.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("You asked for this close, so someone else has to decide it.", await TitleAsync(own));
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/ledger/closes/{close.Id}/send-back", new { reason = "Wrong amount" })).StatusCode);

        var sendBack = $"/api/ledger/closes/{close.Id}/send-back";
        Assert.Equal(HttpStatusCode.BadRequest, (await salesManager.Client.PostAsJsonAsync(sendBack, new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await salesManager.Client.PostAsJsonAsync(sendBack, new { reason = "Customer still wants it" })).StatusCode);

        var after = (await StatementAsync(admin, w.CustomerId))!.Credits.Single();
        Assert.Equal(5m, after.BalanceCft);
        var entry = after.Entries.Single(e => e.Kind == LedgerEntryKind.Closed);
        Assert.Equal(LedgerCloseStatus.SentBack, entry.CloseStatus);
        Assert.Equal(0m, entry.QuantityCft);
        Assert.Contains("Customer still wants it", entry.Text);

        var actions = (await AuditAsync(admin)).Select(a => a.Action).ToList();
        Assert.Contains("ledger.close_requested", actions);
        Assert.Contains("ledger.close_sent_back", actions);
    }

    [Fact]
    public async Task The_ledger_needs_its_permissions()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var management = await factory.AddMemberAsync(company, "Management");
        var planner = await factory.AddMemberAsync(company, "Planner");
        var credit = await CreditAsync(await AddCreditAsync(admin, w.CustomerId, w.ProductId, 5m));
        var order = await RunningAsync(admin, w);

        Assert.Equal(HttpStatusCode.Forbidden, (await management.Client.GetAsync("/api/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await management.Client.GetAsync($"/api/customers/{w.CustomerId}/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await management.Client.GetAsync("/api/ledger/closes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.Client.GetAsync($"/api/orders/{order.Order.Id}/ledger-credits")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await UseAsync(planner.Client, order.Order.Id, order.Lines.Single().Id, credit.Id, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await planner.Client.PostAsync($"/api/ledger/closes/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Another_companys_ledger_stays_hidden()
    {
        var a = await factory.CreateCompanyAsync();
        var b = await factory.CreateCompanyAsync();
        var w = await CreateOrderWorldAsync(a.Admin.Client);
        var credit = await CreditAsync(await AddCreditAsync(a.Admin.Client, w.CustomerId, w.ProductId, 5m));
        (await RequestCloseAsync(a.Admin.Client, credit.Id, 1m)).EnsureSuccessStatusCode();
        var close = (await a.Admin.Client.GetFromJsonAsync<List<LedgerCloseDto>>("/api/ledger/closes", ApiFactory.Json))!.Single();

        var other = b.Admin.Client;
        var otherWorld = await CreateOrderWorldAsync(other);
        var otherOrder = await RunningAsync(other, otherWorld);

        Assert.Empty((await other.GetFromJsonAsync<PagedResult<LedgerCustomerDto>>("/api/ledger?all=true", ApiFactory.Json))!.Items);
        Assert.Empty((await other.GetFromJsonAsync<List<LedgerCloseDto>>("/api/ledger/closes", ApiFactory.Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/customers/{w.CustomerId}/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddCreditAsync(other, w.CustomerId, otherWorld.ProductId, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"/api/ledger/credits/{credit.Id}/corrections", new { quantityCft = 1m, reason = "Recount" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RequestCloseAsync(other, credit.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/ledger/closes/{close.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await UseAsync(other, otherOrder.Order.Id, otherOrder.Lines.Single().Id, credit.Id, 1m)).StatusCode);
    }
}
