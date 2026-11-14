using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class OrderConfirmationTests(ApiFactory factory)
{
    private static Task<OrderDetailDto?> GetAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", ApiFactory.Json);

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client, Guid id, string method = "WrittenPo", string? reference = "PO-77") =>
        client.PostAsJsonAsync($"/api/orders/{id}/confirm", new { method, reference });

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid id, string route, bool approve, string? comment = null) =>
        client.PostAsJsonAsync($"/api/orders/{id}/approvals/{route}/{(approve ? "approve" : "reject")}", new { comment });

    // Submitted and approved by the sales manager. The list price is 120 and the floor is 100.
    private static async Task<OrderDetailDto> ApprovedAsync(HttpClient admin, OrderWorld w, decimal qty = 10m)
    {
        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, qty)]);
        (await admin.PostAsync($"/api/orders/{draft.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        (await DecideAsync(admin, draft.Order.Id, "sales-manager", true)).EnsureSuccessStatusCode();
        return (await GetAsync(admin, draft.Order.Id))!;
    }

    private static async Task<OrderDetailDto> ConfirmedAsync(HttpClient admin, OrderWorld w, decimal qty = 10m)
    {
        var order = await ApprovedAsync(admin, w, qty);
        (await ConfirmAsync(admin, order.Order.Id)).EnsureSuccessStatusCode();
        return (await GetAsync(admin, order.Order.Id))!;
    }

    private static Task<HttpResponseMessage> AmendAsync(
        HttpClient client, OrderWorld w, OrderDetailDto order, decimal qty, decimal price,
        string reason = "Customer changed the order", string? notes = null)
    {
        var line = order.Lines.Single();
        return client.PutAsJsonAsync($"/api/orders/{order.Order.Id}/amend", new
        {
            reason,
            order = new
            {
                customerId = w.CustomerId,
                siteId = w.SiteId,
                plantId = w.PlantId,
                requestedDate = "2026-06-01",
                distanceKm = 5m,
                notes,
                lines = new[]
                {
                    new { id = line.Id, productId = line.ProductId, quantityCft = qty, unitPrice = price,
                          pourElement = "Slab", pourMethod = "Chute" }
                }
            }
        });
    }

    [Fact]
    public async Task Confirming_an_approved_order_records_the_details_and_locks_it()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(admin, draft.Order.Id)).StatusCode);
        (await admin.PostAsync($"/api/orders/{draft.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(admin, draft.Order.Id)).StatusCode);
        (await DecideAsync(admin, draft.Order.Id, "sales-manager", true)).EnsureSuccessStatusCode();

        var noMethod = await admin.PostAsJsonAsync($"/api/orders/{draft.Order.Id}/confirm", new { reference = "PO-1" });
        Assert.Equal(HttpStatusCode.BadRequest, noMethod.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(admin, draft.Order.Id)).StatusCode);

        var order = (await GetAsync(admin, draft.Order.Id))!;
        Assert.Equal(OrderStatus.Confirmed, order.Order.Status);
        Assert.Equal(ConfirmationMethod.WrittenPo, order.Order.ConfirmationMethod);
        Assert.Equal("PO-77", order.Order.ConfirmationReference);
        Assert.NotNull(order.Order.ConfirmedAtUtc);
        Assert.NotNull(order.Order.ConfirmedBy);
        Assert.Equal(OrderStatus.Confirmed, order.History.Last().ToStatus);

        // Locked: no edit, no second confirmation.
        var line = order.Lines.Single();
        var edit = await admin.PutAsJsonAsync($"/api/orders/{order.Order.Id}", OrderBody(w, [new
        {
            id = line.Id, productId = line.ProductId, quantityCft = 20m, unitPrice = 120m,
            pourElement = "Slab", pourMethod = "Chute"
        }]));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(admin, order.Order.Id)).StatusCode);
    }

    [Fact]
    public async Task Confirming_needs_its_own_permission_and_stays_inside_the_company()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var w = await CreateOrderWorldAsync(company.Admin.Client);
        var management = await factory.AddMemberAsync(company, "Management");
        var order = await ApprovedAsync(company.Admin.Client, w);

        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(management.Client, order.Order.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(other.Admin.Client, order.Order.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(company.Admin.Client, order.Order.Id)).StatusCode);
    }

    [Fact]
    public async Task An_amendment_that_does_not_raise_the_total_keeps_the_order_confirmed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await ConfirmedAsync(admin, w);   // 10 CFT at 120

        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, order, 10m, 120m, notes: "Call first")).StatusCode);
        var notes = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Confirmed, notes.Order.Status);
        Assert.Equal("Call first", notes.Order.Notes);

        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, notes, 8m, 120m)).StatusCode);
        var smaller = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Confirmed, smaller.Order.Status);
        Assert.Equal(8m, smaller.Lines.Single().QuantityCft);
        Assert.Equal(order.Approvals.Count, smaller.Approvals.Count);
    }

    [Fact]
    public async Task An_amendment_that_raises_the_total_goes_back_to_management_and_then_to_confirmed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await ConfirmedAsync(admin, w);

        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, order, 12m, 120m)).StatusCode);

        var waiting = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, waiting.Order.Status);
        Assert.Equal(12m, waiting.Lines.Single().QuantityCft);
        Assert.Contains(waiting.Approvals, a => a.Stage == ApprovalStage.Management && a.Decision == ApprovalDecision.Pending);
        Assert.Contains(waiting.Approvals, a => a.Stage == ApprovalStage.SalesManager && a.Decision == ApprovalDecision.Approved);

        // The amended order is locked for anything but a revision while it waits.
        Assert.Equal(HttpStatusCode.Conflict, (await AmendAsync(admin, w, waiting, 13m, 120m)).StatusCode);

        (await DecideAsync(admin, order.Order.Id, "management", true, "Fine")).EnsureSuccessStatusCode();
        var done = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Confirmed, done.Order.Status);
        Assert.Equal(
            [OrderStatus.Draft, OrderStatus.Submitted, OrderStatus.Approved, OrderStatus.Confirmed,
             OrderStatus.InApproval, OrderStatus.Confirmed],
            done.History.Select(h => h.ToStatus));

        // 12 CFT is now the approved size, so going down and back to it needs no approval.
        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, done, 12m, 120m)).StatusCode);
        Assert.Equal(OrderStatus.Confirmed, (await GetAsync(admin, order.Order.Id))!.Order.Status);
    }

    [Fact]
    public async Task A_rejected_amendment_returns_the_order_to_draft_and_clears_the_confirmation()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await ConfirmedAsync(admin, w);

        // A lower price below the floor of 100.
        Assert.Equal(HttpStatusCode.NoContent, (await AmendAsync(admin, w, order, 10m, 95m)).StatusCode);
        var waiting = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, waiting.Order.Status);
        Assert.Equal(1, waiting.Order.BelowFloorLineCount);

        (await DecideAsync(admin, order.Order.Id, "management", false, "Too low")).EnsureSuccessStatusCode();
        var draft = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Draft, draft.Order.Status);
        Assert.Null(draft.Order.ConfirmedAtUtc);
        Assert.Null(draft.Order.ConfirmationMethod);
        Assert.Equal("Too low", draft.History.Last().Reason);
    }

    [Fact]
    public async Task Amending_has_rules()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var other = await factory.CreateCompanyAsync();
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var approved = await ApprovedAsync(admin, w);
        Assert.Equal(HttpStatusCode.Conflict, (await AmendAsync(admin, w, approved, 10m, 120m)).StatusCode);

        (await ConfirmAsync(admin, approved.Order.Id)).EnsureSuccessStatusCode();
        var confirmed = (await GetAsync(admin, approved.Order.Id))!;

        Assert.Equal(HttpStatusCode.BadRequest, (await AmendAsync(admin, w, confirmed, 10m, 120m, reason: "")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AmendAsync(rep.Client, w, confirmed, 10m, 120m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AmendAsync(other.Admin.Client, w, confirmed, 10m, 120m)).StatusCode);

        var audit = await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit?take=100", ApiFactory.Json);
        Assert.Contains(audit!, e => e.Action == "order.confirmed" && e.EntityId == confirmed.Order.Id.ToString());
    }

    [Fact]
    public async Task A_confirmed_order_can_be_held_released_and_cancelled()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var approved = await ApprovedAsync(admin, w);
        var holdApproved = await admin.PostAsJsonAsync($"/api/orders/{approved.Order.Id}/hold", new { reason = "Waiting" });
        Assert.Equal(HttpStatusCode.Conflict, holdApproved.StatusCode);

        var order = await ConfirmedAsync(admin, w);
        var id = order.Order.Id;

        var noReason = await admin.PostAsJsonAsync($"/api/orders/{id}/hold", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/orders/{id}/release", null)).StatusCode);

        var hold = await admin.PostAsJsonAsync($"/api/orders/{id}/hold", new { reason = "Waiting for site access" });
        Assert.Equal(HttpStatusCode.NoContent, hold.StatusCode);
        var held = (await GetAsync(admin, id))!;
        Assert.Equal(OrderStatus.OnHold, held.Order.Status);
        Assert.Equal("Waiting for site access", held.Order.HoldReason);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/orders/{id}/hold", new { reason = "Again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AmendAsync(admin, w, held, 5m, 120m)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/orders/{id}/release", null)).StatusCode);
        var released = (await GetAsync(admin, id))!;
        Assert.Equal(OrderStatus.Confirmed, released.Order.Status);
        Assert.Null(released.Order.HoldReason);

        // A held order can still be cancelled.
        (await admin.PostAsJsonAsync($"/api/orders/{id}/hold", new { reason = "Site closed" })).EnsureSuccessStatusCode();
        var cancel = await admin.PostAsJsonAsync($"/api/orders/{id}/cancel", new { reason = "Customer gave up" });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await GetAsync(admin, id))!.Order.Status);
    }
}