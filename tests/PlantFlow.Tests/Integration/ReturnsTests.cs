using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Planning;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

public partial class PourPlanningTests
{
    private record Pair(Guid PlanId, Guid PourA, Guid PourB, Guid Truck);

    // One approved shift plan with two pours of the same product: A (100 CFT, pumped) and B (50 CFT, no pump).
    private async Task<Pair> TwoPoursAsync(HttpClient admin)
    {
        var fx = await SetupAsync(admin);

        async Task<Guid> ConfirmedLineAsync(object line)
        {
            var created = await admin.PostAsJsonAsync("/api/orders", new
            {
                customerId = fx.W.CustomerId,
                siteId = fx.W.SiteId,
                plantId = fx.W.PlantId,
                requestedDate = "2026-06-01",
                distanceKm = 5m,
                lines = new[] { line }
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var orderId = (await created.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!.Order.Id;

            (await admin.PostAsync($"/api/orders/{orderId}/submit", null)).EnsureSuccessStatusCode();
            (await admin.PostAsJsonAsync($"/api/orders/{orderId}/approvals/sales-manager/approve", new { comment = (string?)null })).EnsureSuccessStatusCode();
            (await admin.PostAsJsonAsync($"/api/orders/{orderId}/confirm", new { method = "Phone" })).EnsureSuccessStatusCode();
            var confirmed = (await admin.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{orderId}", ApiFactory.Json))!;
            return confirmed.Lines.Single().Id;
        }

        var lineA = await ConfirmedLineAsync(LineBody(fx.W.ProductId, 100m, null, "Slab", "Pump"));
        var lineB = await ConfirmedLineAsync(LineBody(fx.W.ProductId, 50m));
        var plan = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);

        var a = await PourAsync(admin, plan.Plan.Id, lineA, 100m, "09:00:00", fx.Pump);
        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        var b = await PourAsync(admin, plan.Plan.Id, lineB, 50m, "10:00:00", null);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);

        (await admin.PostAsJsonAsync($"/api/shift-plans/{plan.Plan.Id}/submit", new { })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/shift-plans/{plan.Plan.Id}/approve", new { })).EnsureSuccessStatusCode();

        return new Pair(
            plan.Plan.Id,
            (await a.Content.ReadFromJsonAsync<PourDto>(ApiFactory.Json))!.Id,
            (await b.Content.ReadFromJsonAsync<PourDto>(ApiFactory.Json))!.Id,
            fx.Pump2);
    }

    private static async Task<LoadDto> ProduceLoadAsync(HttpClient c, Guid pourId, Guid truck, Guid? returnedConcreteId = null)
    {
        var response = await c.PostAsJsonAsync($"/api/pours/{pourId}/loads", new { truckId = truck, returnedConcreteId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;
    }

    // Dispatch, arrive, pour and return, in order.
    private static async Task CompleteLoadAsync(HttpClient c, Guid loadId)
    {
        var now = DateTime.UtcNow;
        (await c.PostAsJsonAsync($"/api/loads/{loadId}/dispatch", new { })).EnsureSuccessStatusCode();
        (await Step(c, loadId, "Arrived", now.AddMinutes(1))).EnsureSuccessStatusCode();
        (await Step(c, loadId, "PouringStarted", now.AddMinutes(2))).EnsureSuccessStatusCode();
        (await Step(c, loadId, "PouringEnded", now.AddMinutes(3))).EnsureSuccessStatusCode();
        (await Step(c, loadId, "Returned", now.AddMinutes(4))).EnsureSuccessStatusCode();
    }

    private static async Task<PourProductionDto> PourViewAsync(HttpClient c, Guid planId, Guid pourId)
    {
        var view = await c.GetFromJsonAsync<ShiftProductionDto>($"/api/shift-plans/{planId}/operator", ApiFactory.Json);
        return view!.Pours.Single(p => p.PourId == pourId);
    }

    private static Task<HttpResponseMessage> DecideAsync(HttpClient c, Guid returnId, string decision, string? note = null) =>
        c.PostAsJsonAsync($"/api/returns/{returnId}/decide", new { decision, note });

    [Fact]
    public async Task A_site_reject_is_inspected_treated_and_delivered_again_without_being_billed()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await TwoPoursAsync(admin);
        (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).EnsureSuccessStatusCode();

        var load = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        Assert.Equal(60m, (await PourViewAsync(admin, s.PlanId, s.PourA)).RemainingCft);

        // A load on the road cannot be rejected until it arrives. A reject at the site needs a fault.
        (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/reject", new { fault = "Company", reason = "Slump too low" })).StatusCode);
        (await Step(admin, load.Id, "Arrived", DateTime.UtcNow.AddMinutes(1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/loads/{load.Id}/reject", new { reason = "Slump too low" })).StatusCode);

        var rejection = await admin.PostAsJsonAsync($"/api/loads/{load.Id}/reject", new { fault = "Company", reason = "Slump too low" });
        Assert.Equal(HttpStatusCode.Created, rejection.StatusCode);
        var returned = (await rejection.Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        Assert.Equal(ReturnKind.RejectedAtSite, returned.Kind);
        Assert.Equal(ReturnStatus.AwaitingInspection, returned.Status);
        Assert.Equal(40m, returned.RemainingCft);

        // The rejected load no longer counts as produced, and nothing of it was delivered.
        var afterReject = await PourViewAsync(admin, s.PlanId, s.PourA);
        Assert.Equal(100m, afterReject.RemainingCft);
        Assert.Equal(0m, afterReject.DeliveredCft);
        Assert.Equal("Rejected", afterReject.Loads.Single().Stage);

        // The truck is back, so it can be used again.
        Assert.Equal(HttpStatusCode.OK, (await Step(admin, load.Id, "Returned", DateTime.UtcNow.AddMinutes(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Step(admin, load.Id, "PouringStarted")).StatusCode);

        // Concrete that has not been inspected cannot be used.
        var tooSoon = await admin.PostAsJsonAsync($"/api/pours/{s.PourA}/loads", new { truckId = s.Truck, returnedConcreteId = returned.Id });
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);

        // Inspection: treating comes before treated, and nothing skips ahead.
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(admin, returned.Id, "Treated")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(admin, returned.Id, "Dumped")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, returned.Id, "NeedsTreatment", "Add admixture")).StatusCode);
        var treated = await DecideAsync(admin, returned.Id, "Treated", "Re-mixed and slump checked");
        Assert.Equal(HttpStatusCode.OK, treated.StatusCode);
        Assert.Equal(ReturnStatus.Treated, (await treated.Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!.Status);

        // The treated concrete goes back to the site as a new load made from it.
        var replacement = await ProduceLoadAsync(admin, s.PourA, s.Truck, returned.Id);
        Assert.Equal(40m, replacement.QuantityCft);
        Assert.Equal(returned.Id, replacement.SourceReturnId);

        var stock = await admin.GetFromJsonAsync<List<ReturnedConcreteDto>>($"/api/returns?shiftPlanId={s.PlanId}", ApiFactory.Json);
        var used = Assert.Single(stock!);
        Assert.Equal(ReturnStatus.Used, used.Status);
        Assert.Equal(0m, used.RemainingCft);

        await CompleteLoadAsync(admin, replacement.Id);

        // The customer is billed for the replacement only.
        Assert.Equal(40m, (await PourViewAsync(admin, s.PlanId, s.PourA)).DeliveredCft);
    }

    [Fact]
    public async Task A_part_return_is_netted_and_reused_or_dumped_by_fault_and_a_plant_reject_is_the_companys()
    {
        var company = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var s = await TwoPoursAsync(admin);
        (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).EnsureSuccessStatusCode();

        // A 40 CFT load is poured, then 5 CFT come back.
        var first = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        var early = await admin.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 5m, fault = "Customer", reason = "Job finished early" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        await CompleteLoadAsync(admin, first.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 5m, fault = "Customer", reason = "Job finished early" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.Client.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 40m, fault = "Customer", reason = "Job finished early" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.Client.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 5m, reason = "Job finished early" })).StatusCode);

        var part = await manager.Client.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 5m, fault = "Customer", reason = "Job finished early" });
        Assert.Equal(HttpStatusCode.Created, part.StatusCode);
        var leftover = (await part.Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        Assert.Equal(ReturnKind.PartLoad, leftover.Kind);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Client.PostAsJsonAsync($"/api/loads/{first.Id}/part-return",
            new { quantityCft = 2m, fault = "Customer", reason = "Again" })).StatusCode);

        // Delivered is what was poured less what came back, and the pour still counts as produced.
        var pourA = await PourViewAsync(admin, s.PlanId, s.PourA);
        Assert.Equal(35m, pourA.DeliveredCft);
        Assert.Equal(40m, pourA.ProducedCft);

        // The leftover is inspected and reused on pour B, in the same shift. The first customer is not billed for it.
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(rep.Client, leftover.Id, "Reusable")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(manager.Client, leftover.Id, "Reusable")).StatusCode);
        var reused = await ProduceLoadAsync(admin, s.PourB, s.Truck, leftover.Id);
        Assert.Equal(5m, reused.QuantityCft);
        Assert.Equal(35m, (await PourViewAsync(admin, s.PlanId, s.PourA)).DeliveredCft);
        await CompleteLoadAsync(admin, reused.Id);

        // A second 40 CFT load comes back with 10 CFT. The customer is at fault, and it is dumped, so it is billed.
        var second = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        await CompleteLoadAsync(admin, second.Id);
        var dumpedPart = (await (await admin.PostAsJsonAsync($"/api/loads/{second.Id}/part-return",
            new { quantityCft = 10m, fault = "Customer", reason = "Customer over-ordered" }))
            .Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        Assert.Equal(65m, (await PourViewAsync(admin, s.PlanId, s.PourA)).DeliveredCft);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, dumpedPart.Id, "Dumped", "Set too hard")).StatusCode);
        Assert.Equal(75m, (await PourViewAsync(admin, s.PlanId, s.PourA)).DeliveredCft);

        // Dumped is final.
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(admin, dumpedPart.Id, "Reusable")).StatusCode);

        // The company is at fault for a part of the last load. Dumped, it is not billed.
        var third = await ProduceLoadAsync(admin, s.PourA, s.Truck);
        Assert.Equal(20m, third.QuantityCft);
        await CompleteLoadAsync(admin, third.Id);
        var companyPart = (await (await admin.PostAsJsonAsync($"/api/loads/{third.Id}/part-return",
            new { quantityCft = 4m, fault = "Company", reason = "Slump drifted" }))
            .Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, companyPart.Id, "Dumped", "Unusable")).StatusCode);
        Assert.Equal(91m, (await PourViewAsync(admin, s.PlanId, s.PourA)).DeliveredCft);

        // A reject at the plant is always the company's fault, and the truck is free at once.
        var bad = await ProduceLoadAsync(admin, s.PourB, s.Truck);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/loads/{bad.Id}/reject", new { fault = "Customer", reason = "Failed QC" })).StatusCode);
        var plantReject = await admin.PostAsJsonAsync($"/api/loads/{bad.Id}/reject", new { reason = "Failed QC" });
        Assert.Equal(HttpStatusCode.Created, plantReject.StatusCode);
        var plantRecord = (await plantReject.Content.ReadFromJsonAsync<ReturnedConcreteDto>(ApiFactory.Json))!;
        Assert.Equal(ReturnKind.RejectedAtPlant, plantRecord.Kind);
        Assert.Equal(ReturnFault.Company, plantRecord.Fault);

        var replacementLoad = await ProduceLoadAsync(admin, s.PourB, s.Truck);
        Assert.Equal(40m, replacementLoad.QuantityCft);

        // A load made from returned concrete that is then cancelled puts the concrete back.
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(admin, plantRecord.Id, "Reusable")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/loads/{replacementLoad.Id}/cancel", new { reason = "Wrong truck" })).StatusCode);
        var fromStock = await ProduceLoadAsync(admin, s.PourB, s.Truck, plantRecord.Id);
        Assert.Equal(40m, fromStock.QuantityCft);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/loads/{fromStock.Id}/cancel", new { reason = "Changed plan" })).StatusCode);
        var back = await admin.GetFromJsonAsync<List<ReturnedConcreteDto>>($"/api/returns?status=Reusable", ApiFactory.Json);
        Assert.Equal(40m, back!.Single(r => r.Id == plantRecord.Id).RemainingCft);
    }
}