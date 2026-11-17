using System.Net;
using System.Net.Http.Json;
using PlantFlow.Api.Controllers;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;
using static PlantFlow.Tests.Integration.TestData;

namespace PlantFlow.Tests.Integration;

public partial class PourPlanningTests
{
    private record Scene(Guid PlanId, Guid PourId, Guid OrderId, Guid Truck, Guid Pump);

    // A confirmed pumped order with one pour planned. A transit mixer carries the loads.
    private async Task<Scene> ApprovedPourAsync(HttpClient admin, decimal cft = 100m, bool approve = true)
    {
        var fx = await SetupAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/orders", new
        {
            customerId = fx.W.CustomerId,
            siteId = fx.W.SiteId,
            plantId = fx.W.PlantId,
            requestedDate = "2026-06-01",
            distanceKm = 5m,
            lines = new[] { LineBody(fx.W.ProductId, cft, null, "Slab", "Pump") }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var orderId = (await created.Content.ReadFromJsonAsync<OrderDetailDto>(ApiFactory.Json))!.Order.Id;

        (await admin.PostAsync($"/api/orders/{orderId}/submit", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/approvals/sales-manager/approve", new { comment = (string?)null })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/confirm", new { method = "Phone" })).EnsureSuccessStatusCode();
        var confirmed = (await admin.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{orderId}", ApiFactory.Json))!;

        var plan = await PlanAsync(admin, fx.W.PlantId, "2026-06-01", fx.Morning);
        var pourResponse = await PourAsync(admin, plan.Plan.Id, confirmed.Lines.Single().Id, cft, "09:00:00", fx.Pump);
        Assert.Equal(HttpStatusCode.Created, pourResponse.StatusCode);
        var pour = (await pourResponse.Content.ReadFromJsonAsync<PourDto>(ApiFactory.Json))!;

        if (approve)
        {
            (await admin.PostAsJsonAsync($"/api/shift-plans/{plan.Plan.Id}/submit", new { })).EnsureSuccessStatusCode();
            (await admin.PostAsJsonAsync($"/api/shift-plans/{plan.Plan.Id}/approve", new { })).EnsureSuccessStatusCode();
        }

        return new Scene(plan.Plan.Id, pour.Id, orderId, fx.Mixer, fx.Pump);
    }

    private static Task<HttpResponseMessage> Step(HttpClient c, Guid loadId, string step, DateTime? at = null) =>
        c.PostAsJsonAsync($"/api/loads/{loadId}/steps", new { step, at });

    [Fact]
    public async Task A_load_is_produced_dispatched_followed_and_poured_into_the_delivered_quantity()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await ApprovedPourAsync(admin, 100m);
        var now = DateTime.UtcNow;

        async Task<ShiftProductionDto> ViewAsync() =>
            (await admin.GetFromJsonAsync<ShiftProductionDto>($"/api/shift-plans/{s.PlanId}/operator", ApiFactory.Json))!;

        async Task<LoadDto> ProduceAsync(object? body = null)
        {
            var response = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", body ?? new { truckId = s.Truck });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;
        }

        // The truck carries 40 CFT, so that is the usual load.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 40m })).StatusCode);

        var start = await ViewAsync();
        Assert.Equal(100m, start.PlannedCft);
        Assert.Equal(0m, start.ProducedCft);
        Assert.Equal(100m, start.RemainingCft);
        Assert.Equal("Ok", start.Pours.Single().Survey!.Result.ToString());

        var first = await ProduceAsync();
        Assert.Equal(40m, first.QuantityCft);
        Assert.Equal(1, first.Number);
        Assert.Equal("Produced", first.Stage);
        Assert.Equal(60m, (await ViewAsync()).RemainingCft);

        // The truck is out until it returns.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", new { truckId = s.Truck })).StatusCode);

        // Steps need the one before, in order, and never in the future.
        Assert.Equal(HttpStatusCode.Conflict, (await Step(admin, first.Id, "Arrived")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/loads/{first.Id}/dispatch", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/loads/{first.Id}/dispatch", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Step(admin, first.Id, "PouringEnded")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Step(admin, first.Id, "Arrived", now.AddHours(1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Step(admin, first.Id, "Arrived", now.AddMinutes(-30))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Step(admin, first.Id, "Arrived", now.AddMinutes(1))).StatusCode);

        // Pouring cannot start before the pump is ready.
        Assert.Equal(HttpStatusCode.Conflict, (await Step(admin, first.Id, "PouringStarted", now.AddMinutes(2))).StatusCode);
        await PumpReadyAsync(admin, s.PourId);
        Assert.Equal(HttpStatusCode.OK, (await Step(admin, first.Id, "PouringStarted", now.AddMinutes(2))).StatusCode);

        // A cancel is no longer possible once pouring has started.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/loads/{first.Id}/cancel", new { reason = "Changed our mind" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Step(admin, first.Id, "PouringEnded", now.AddMinutes(3))).StatusCode);

        // A time can be corrected, but not past the next step.
        Assert.Equal(HttpStatusCode.OK, (await Step(admin, first.Id, "PouringStarted", now.AddSeconds(150))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Step(admin, first.Id, "PouringStarted", now.AddMinutes(4))).StatusCode);

        // The poured quantity is now what was delivered, and the order is running.
        Assert.Equal(40m, (await ViewAsync()).DeliveredCft);
        var order = (await admin.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{s.OrderId}", ApiFactory.Json))!;
        Assert.Equal(OrderStatus.InProgress, order.Order.Status);

        Assert.Equal(HttpStatusCode.OK, (await Step(admin, first.Id, "Returned", now.AddMinutes(4))).StatusCode);

        // The truck is back, so it can carry the next load. The last one is only what is left.
        var second = await ProduceAsync();
        Assert.Equal(40m, second.QuantityCft);
        (await admin.PostAsJsonAsync($"/api/loads/{second.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        // This one never reaches the pour, so its quantity returns to the pour.
        var cancelled = await admin.PostAsJsonAsync($"/api/loads/{second.Id}/cancel", new { reason = "Truck broke down" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!.Stage);
        Assert.Equal(60m, (await ViewAsync()).RemainingCft);

        var third = await ProduceAsync();
        Assert.Equal(40m, third.QuantityCft);
        Assert.Equal(3, third.Number);
        (await admin.PostAsJsonAsync($"/api/loads/{third.Id}/cancel", new { reason = "Wrong truck" })).EnsureSuccessStatusCode();

        // Use up the pour: 40, then the last 20 without a reason.
        var fourth = await ProduceAsync();
        (await admin.PostAsJsonAsync($"/api/loads/{fourth.Id}/cancel", new { reason = "Wrong truck again" })).EnsureSuccessStatusCode();
        var fifth = await ProduceAsync();
        (await Step(admin, fifth.Id, "Arrived")).Dispose();
        (await admin.PostAsJsonAsync($"/api/loads/{fifth.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        (await Step(admin, fifth.Id, "Arrived", now.AddMinutes(1))).EnsureSuccessStatusCode();
        (await Step(admin, fifth.Id, "PouringStarted", now.AddMinutes(2))).EnsureSuccessStatusCode();
        (await Step(admin, fifth.Id, "PouringEnded", now.AddMinutes(3))).EnsureSuccessStatusCode();
        (await Step(admin, fifth.Id, "Returned", now.AddMinutes(4))).EnsureSuccessStatusCode();
        Assert.Equal(80m, (await ViewAsync()).DeliveredCft);

        var last = await ProduceAsync();
        Assert.Equal(20m, last.QuantityCft);
        Assert.Equal(0m, (await ViewAsync()).RemainingCft);
        (await admin.PostAsJsonAsync($"/api/loads/{last.Id}/dispatch", new { })).EnsureSuccessStatusCode();
        (await Step(admin, last.Id, "Arrived", now.AddMinutes(1))).EnsureSuccessStatusCode();
        (await Step(admin, last.Id, "PouringStarted", now.AddMinutes(2))).EnsureSuccessStatusCode();
        (await Step(admin, last.Id, "PouringEnded", now.AddMinutes(3))).EnsureSuccessStatusCode();
        (await Step(admin, last.Id, "Returned", now.AddMinutes(4))).EnsureSuccessStatusCode();
        Assert.Equal(100m, (await ViewAsync()).DeliveredCft);

        // The pour is complete. More needs the plant manager's override and a reason.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", new { truckId = s.Truck })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads/override", new { truckId = s.Truck })).StatusCode);
        var extra = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads/override",
            new { truckId = s.Truck, overrideReason = "Customer asked for a little more" });
        Assert.Equal(HttpStatusCode.Created, extra.StatusCode);
        Assert.StartsWith("Over plan:", (await extra.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!.QuantityReason);
    }

    [Fact]
    public async Task Pump_steps_are_recorded_in_order()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await ApprovedPourAsync(admin);
        var now = DateTime.UtcNow;
        Task<HttpResponseMessage> PumpStep(string step, DateTime? at = null) =>
            admin.PostAsJsonAsync($"/api/pours/{s.PourId}/pump-steps", new { step, at });

        Assert.Equal(HttpStatusCode.Conflict, (await PumpStep("Assembled")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PumpStep("Dispatched", now.AddMinutes(-0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PumpStep("Arrived", now.AddMinutes(-10))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PumpStep("Arrived", now.AddMinutes(1))).StatusCode);
        var assembled = await PumpStep("Assembled", now.AddMinutes(2));
        Assert.Equal(HttpStatusCode.OK, assembled.StatusCode);

        var pour = (await assembled.Content.ReadFromJsonAsync<PourProductionDto>(ApiFactory.Json))!;
        Assert.NotNull(pour.PumpDispatchedAtUtc);
        Assert.NotNull(pour.PumpArrivedAtUtc);
        Assert.NotNull(pour.PumpAssembledAtUtc);
    }

    [Fact]
    public async Task Production_follows_the_load_rules_and_permissions()
    {
        var company = await factory.CreateCompanyAsync();
        var other = await factory.CreateCompanyAsync();
        var admin = company.Admin.Client;
        var manager = await factory.AddMemberAsync(company, "Plant Manager");
        var rep = await factory.AddMemberAsync(company, "Sales Representative");
        var s = await ApprovedPourAsync(admin);
        var loads = $"/api/pours/{s.PourId}/loads";

        // The plant manager runs the plant. A sales rep sees none of it.
        Assert.Equal(HttpStatusCode.OK, (await manager.Client.GetAsync($"/api/shift-plans/{s.PlanId}/operator")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Client.GetAsync($"/api/shift-plans/{s.PlanId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.GetAsync($"/api/shift-plans/{s.PlanId}/operator")).StatusCode);
        // Reps can look at dashboards, but only the plant can produce and dispatch.
        Assert.Equal(HttpStatusCode.OK, (await rep.Client.GetAsync($"/api/shift-plans/{s.PlanId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Client.PostAsJsonAsync(loads, new { truckId = s.Truck })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rep.Client.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 30m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rep.Client.PostAsJsonAsync($"/api/pours/{s.PourId}/loads/override",
                new { truckId = s.Truck, overrideReason = "Nope" })).StatusCode);

        // Capacity: bad values are refused, and clearing it goes back to the company's load size.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.Client.PutAsJsonAsync($"/api/vehicles/{s.Truck}/capacity", new { capacityCft = (decimal?)null })).StatusCode);

        // A load can't be bigger than the truck's load size (100), and a smaller one needs a reason.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PostAsJsonAsync(loads, new { truckId = s.Truck, quantityCft = 150m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PostAsJsonAsync(loads, new { truckId = s.Truck, quantityCft = 10.123m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PostAsJsonAsync(loads, new { truckId = s.Truck, quantityCft = 30m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PostAsJsonAsync(loads, new { truckId = Guid.NewGuid() })).StatusCode);
        // The pump cannot carry concrete.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.Client.PostAsJsonAsync(loads, new { truckId = s.Pump })).StatusCode);

        var small = await manager.Client.PostAsJsonAsync(loads, new { truckId = s.Truck, quantityCft = 30m, reason = "Small pour first" });
        Assert.Equal(HttpStatusCode.Created, small.StatusCode);
        var load = (await small.Content.ReadFromJsonAsync<LoadDto>(ApiFactory.Json))!;
        Assert.Equal(30m, load.QuantityCft);
        Assert.Equal("Small pour first", load.QuantityReason);

        // Another company sees none of it.
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.GetAsync($"/api/shift-plans/{s.PlanId}/operator")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.PostAsJsonAsync(loads, new { truckId = s.Truck })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.Client.PostAsJsonAsync($"/api/loads/{load.Id}/dispatch", new { })).StatusCode);
    }

    [Fact]
    public async Task Nothing_is_produced_for_a_plan_that_is_not_approved()
    {
        var admin = (await factory.CreateCompanyAsync()).Admin.Client;
        var s = await ApprovedPourAsync(admin, approve: false);

        var response = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/loads", new { truckId = s.Truck });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var pump = await admin.PostAsJsonAsync($"/api/pours/{s.PourId}/pump-steps", new { step = "Dispatched" });
        Assert.Equal(HttpStatusCode.Conflict, pump.StatusCode);
    }
}