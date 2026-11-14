using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

[Collection("api")]
public class OrderApprovalTests(ApiFactory factory)
{
    private const string SalesManager = "sales-manager";
    private const string Management = "management";
    private const string Credit = "credit";

    private static Task<HttpResponseMessage> DecideAsync(
        HttpClient client, Guid id, string route, bool approve, string? comment = null) =>
        client.PostAsJsonAsync($"/api/orders/{id}/approvals/{route}/{(approve ? "approve" : "reject")}", new { comment });

    private static Task<OrderDetailDto?> GetAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", ApiFactory.Json);

    // A one-line order that has been submitted. Price 95 is below the floor of 100. No price means the list price of 120.
    private static async Task<OrderDetailDto> SubmittedAsync(HttpClient admin, OrderWorld w, decimal? unitPrice = null)
    {
        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m, unitPrice)]);
        (await admin.PostAsync($"/api/orders/{draft.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        return (await GetAsync(admin, draft.Order.Id))!;
    }

    private static bool Has(OrderDetailDto d, ApprovalStage stage, ApprovalDecision decision, int? round = null) =>
        d.Approvals.Any(a => a.Stage == stage && a.Decision == decision && (round is null || a.Round == round));

    private static Task<HttpResponseMessage> ReviseAsync(
        HttpClient client, OrderWorld w, OrderDetailDto order, decimal unitPrice,
        string reason = "Adjusted by the manager", string? notes = null)
    {
        var line = order.Lines.Single();
        return client.PutAsJsonAsync($"/api/orders/{order.Order.Id}/revise", new
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
                    new { id = line.Id, productId = line.ProductId, quantityCft = line.QuantityCft, unitPrice,
                          pourElement = "Slab", pourMethod = "Chute" }
                }
            }
        });
    }

    [Fact]
    public async Task An_order_at_or_above_the_floor_needs_only_the_sales_manager()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);

        var order = await SubmittedAsync(admin, w);
        Assert.Equal(OrderStatus.Submitted, order.Order.Status);
        Assert.Equal(0, order.Order.BelowFloorLineCount);
        Assert.True(Has(order, ApprovalStage.SalesManager, ApprovalDecision.Pending, round: 1));
        Assert.Single(order.Approvals);

        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(admin, order.Order.Id, SalesManager, true, "Fine")).StatusCode);

        var done = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Approved, done.Order.Status);
        Assert.Equal(ApprovalDecision.Approved, Assert.Single(done.Approvals).Decision);
        Assert.Equal([OrderStatus.Draft, OrderStatus.Submitted, OrderStatus.Approved], done.History.Select(h => h.ToStatus));
    }

    [Fact]
    public async Task A_line_below_the_floor_goes_to_management_after_the_sales_manager()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await SubmittedAsync(admin, w, unitPrice: 95m);
        Assert.Equal(1, order.Order.BelowFloorLineCount);

        // Management cannot act before the manager has.
        var tooEarly = await DecideAsync(admin, order.Order.Id, Management, true);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        (await DecideAsync(admin, order.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();

        var waiting = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, waiting.Order.Status);
        Assert.True(Has(waiting, ApprovalStage.SalesManager, ApprovalDecision.Approved));
        Assert.True(Has(waiting, ApprovalStage.Management, ApprovalDecision.Pending));

        var forManagement = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?awaitingStage=Management", ApiFactory.Json);
        var forManager = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders?awaitingStage=SalesManager", ApiFactory.Json);
        Assert.Equal(order.Order.Id, Assert.Single(forManagement!.Items).Id);
        Assert.Empty(forManager!.Items);

        (await DecideAsync(admin, order.Order.Id, Management, true, "Strategic customer")).EnsureSuccessStatusCode();

        var done = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Approved, done.Order.Status);
        Assert.Equal(
            [OrderStatus.Draft, OrderStatus.Submitted, OrderStatus.InApproval, OrderStatus.Approved],
            done.History.Select(h => h.ToStatus));
    }

    [Fact]
    public async Task A_rejection_returns_the_order_to_draft_and_the_next_submit_starts_a_new_round()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await SubmittedAsync(admin, w, unitPrice: 95m);
        (await DecideAsync(admin, order.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();

        var noComment = await DecideAsync(admin, order.Order.Id, Management, false, "");
        var reject = await DecideAsync(admin, order.Order.Id, Management, false, "Price is too low");
        Assert.Equal(HttpStatusCode.BadRequest, noComment.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var draft = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Draft, draft.Order.Status);
        Assert.True(Has(draft, ApprovalStage.Management, ApprovalDecision.Rejected, round: 1));
        Assert.Equal("Price is too low", draft.History.Last().Reason);

        // The rep raises the price to the floor and submits again.
        var line = draft.Lines.Single();
        var edit = OrderBody(w, [new
        {
            id = line.Id, productId = line.ProductId, quantityCft = line.QuantityCft, unitPrice = 100m,
            pourElement = "Slab", pourMethod = "Chute"
        }]);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/orders/{order.Order.Id}", edit)).StatusCode);
        (await admin.PostAsync($"/api/orders/{order.Order.Id}/submit", null)).EnsureSuccessStatusCode();

        var again = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Submitted, again.Order.Status);
        Assert.Equal(3, again.Approvals.Count);   // the old round stays on record
        Assert.True(Has(again, ApprovalStage.SalesManager, ApprovalDecision.Pending, round: 2));
        Assert.True(Has(again, ApprovalStage.Management, ApprovalDecision.Rejected, round: 1));

        // At the floor, only the manager is needed this time.
        (await DecideAsync(admin, order.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        Assert.Equal(OrderStatus.Approved, (await GetAsync(admin, order.Order.Id))!.Order.Status);
    }

    [Fact]
    public async Task A_commercial_revision_restarts_management_and_can_make_it_unnecessary()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var order = await SubmittedAsync(admin, w, unitPrice: 95m);
        (await DecideAsync(admin, order.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        var waiting = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, waiting.Order.Status);

        // Still below the floor, but the price changed: Management must look again.
        Assert.Equal(HttpStatusCode.NoContent, (await ReviseAsync(admin, w, waiting, 90m)).StatusCode);
        var restarted = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, restarted.Order.Status);
        Assert.Equal(90m, restarted.Lines.Single().UnitPrice);
        Assert.Equal(1, restarted.Approvals.Count(a => a.Stage == ApprovalStage.Management && a.Decision == ApprovalDecision.Withdrawn));
        Assert.Equal(1, restarted.Approvals.Count(a => a.Stage == ApprovalStage.Management && a.Decision == ApprovalDecision.Pending));
        Assert.True(Has(restarted, ApprovalStage.SalesManager, ApprovalDecision.Approved));

        // Raised above the floor: Management is no longer needed and the order is approved.
        Assert.Equal(HttpStatusCode.NoContent, (await ReviseAsync(admin, w, restarted, 130m)).StatusCode);
        var done = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.Approved, done.Order.Status);
        Assert.DoesNotContain(done.Approvals, a => a.Stage == ApprovalStage.Management && a.Decision == ApprovalDecision.Pending);
        Assert.Equal(2, done.Approvals.Count(a => a.Stage == ApprovalStage.Management && a.Decision == ApprovalDecision.Withdrawn));

        var audit = await admin.GetFromJsonAsync<List<AuditLogDto>>("/api/audit?take=100", ApiFactory.Json);
        Assert.Equal(2, audit!.Count(e => e.Action == "order.revised" && e.EntityId == order.Order.Id.ToString()));
    }

    [Fact]
    public async Task Revising_has_rules_and_a_notes_only_change_keeps_the_approvals()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");

        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m, 95m)]);
        var onDraft = await ReviseAsync(admin, w, draft, 100m);
        Assert.Equal(HttpStatusCode.Conflict, onDraft.StatusCode);

        (await admin.PostAsync($"/api/orders/{draft.Order.Id}/submit", null)).EnsureSuccessStatusCode();
        var submitted = (await GetAsync(admin, draft.Order.Id))!;

        var noReason = await ReviseAsync(admin, w, submitted, 95m, reason: "");
        var asRep = await ReviseAsync(rep.Client, w, submitted, 95m);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asRep.StatusCode);

        (await DecideAsync(admin, draft.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        var before = (await GetAsync(admin, draft.Order.Id))!;

        // Same price and quantity, only the notes: the pending Management approval is untouched.
        var notesOnly = await ReviseAsync(admin, w, before, 95m, notes: "Call the site first");
        Assert.Equal(HttpStatusCode.NoContent, notesOnly.StatusCode);

        var after = (await GetAsync(admin, draft.Order.Id))!;
        Assert.Equal("Call the site first", after.Order.Notes);
        Assert.Equal(before.Approvals.Count, after.Approvals.Count);
        Assert.DoesNotContain(after.Approvals, a => a.Decision == ApprovalDecision.Withdrawn);

        // Once approved, it can no longer be revised.
        (await DecideAsync(admin, draft.Order.Id, Management, true)).EnsureSuccessStatusCode();
        var approved = (await GetAsync(admin, draft.Order.Id))!;
        Assert.Equal(HttpStatusCode.Conflict, (await ReviseAsync(admin, w, approved, 130m)).StatusCode);
    }

    [Fact]
    public async Task The_credit_stage_is_a_company_setting()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var credit = await factory.AddMemberAsync(company, "Credit");

        var settings = (await admin.GetFromJsonAsync<TenantSettingsDto>("/api/settings", ApiFactory.Json))!;
        Assert.False(settings.RequireCreditApproval);

        (await admin.PutAsJsonAsync("/api/settings", new
        {
            settings.Name,
            settings.VolumeUnit,
            settings.PumpFreeAboveCft,
            settings.RequireSurveyBeforePlanning,
            settings.MinMarginPercent,
            settings.PumpRestShifts,
            requireCreditApproval = true
        }, ApiFactory.Json)).EnsureSuccessStatusCode();

        // At the floor: manager, then credit. No Management stage.
        var order = await SubmittedAsync(admin, w);
        (await DecideAsync(admin, order.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        var waiting = (await GetAsync(admin, order.Order.Id))!;
        Assert.Equal(OrderStatus.InApproval, waiting.Order.Status);
        Assert.True(Has(waiting, ApprovalStage.Credit, ApprovalDecision.Pending));
        Assert.DoesNotContain(waiting.Approvals, a => a.Stage == ApprovalStage.Management);

        (await DecideAsync(credit.Client, order.Order.Id, Credit, true, "Within the limit")).EnsureSuccessStatusCode();
        Assert.Equal(OrderStatus.Approved, (await GetAsync(admin, order.Order.Id))!.Order.Status);

        // Below the floor as well: both stages open together, and both must approve.
        var both = await SubmittedAsync(admin, w, unitPrice: 95m);
        (await DecideAsync(admin, both.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        var open = (await GetAsync(admin, both.Order.Id))!;
        Assert.True(Has(open, ApprovalStage.Management, ApprovalDecision.Pending));
        Assert.True(Has(open, ApprovalStage.Credit, ApprovalDecision.Pending));

        (await DecideAsync(credit.Client, both.Order.Id, Credit, true)).EnsureSuccessStatusCode();
        Assert.Equal(OrderStatus.InApproval, (await GetAsync(admin, both.Order.Id))!.Order.Status);
        (await DecideAsync(admin, both.Order.Id, Management, true)).EnsureSuccessStatusCode();
        Assert.Equal(OrderStatus.Approved, (await GetAsync(admin, both.Order.Id))!.Order.Status);
    }

    [Fact]
    public async Task Each_stage_needs_its_own_permission_and_decisions_follow_the_state_of_the_order()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var w = await CreateOrderWorldAsync(admin);
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var manager = await factory.AddMemberAsync(company, "Sales Manager");
        var management = await factory.AddMemberAsync(company, "Management");
        var credit = await factory.AddMemberAsync(company, "Credit");

        var order = await SubmittedAsync(admin, w, unitPrice: 95m);
        var id = order.Order.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(rep.Client, id, SalesManager, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(management.Client, id, SalesManager, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(manager.Client, id, SalesManager, true)).StatusCode);

        // The manager cannot decide the Management stage, and a decided stage cannot be decided twice.
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(manager.Client, id, Management, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(credit.Client, id, Management, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(manager.Client, id, SalesManager, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(management.Client, id, Management, true)).StatusCode);

        // A draft cannot be decided.
        var draft = await CreateDraftAsync(admin, w, [LineBody(w.ProductId, 10m)]);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(admin, draft.Order.Id, SalesManager, true)).StatusCode);

        // Cancelling while approvals are waiting withdraws them.
        var another = await SubmittedAsync(admin, w, unitPrice: 95m);
        (await DecideAsync(admin, another.Order.Id, SalesManager, true)).EnsureSuccessStatusCode();
        var cancel = await admin.PostAsJsonAsync($"/api/orders/{another.Order.Id}/cancel", new { reason = "Customer cancelled" });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var cancelled = (await GetAsync(admin, another.Order.Id))!;
        Assert.Equal(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.True(Has(cancelled, ApprovalStage.Management, ApprovalDecision.Withdrawn));
        Assert.DoesNotContain(cancelled.Approvals, a => a.Decision == ApprovalDecision.Pending);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(admin, another.Order.Id, Management, true)).StatusCode);
    }

    [Fact]
    public async Task Another_company_cannot_decide_or_revise_orders()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var w = await CreateOrderWorldAsync(company.Admin.Client);
        var order = await SubmittedAsync(company.Admin.Client, w, unitPrice: 95m);

        var approve = await DecideAsync(other.Admin.Client, order.Order.Id, SalesManager, true);
        var reject = await DecideAsync(other.Admin.Client, order.Order.Id, SalesManager, false, "Not allowed");
        var revise = await ReviseAsync(other.Admin.Client, w, order, 130m);
        var waiting = await other.Admin.Client.GetFromJsonAsync<PagedResult<OrderSummaryDto>>(
            "/api/orders?awaitingStage=SalesManager", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revise.StatusCode);
        Assert.Empty(waiting!.Items);
        Assert.Equal(OrderStatus.Submitted, (await GetAsync(company.Admin.Client, order.Order.Id))!.Order.Status);
    }
}