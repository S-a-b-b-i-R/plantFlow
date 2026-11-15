using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record LoadDto(
    Guid Id, Guid PourId, int Number, Guid TruckId, decimal QuantityCft, string? QuantityReason, string Stage,
    DateTime ProducedAtUtc, DateTime? DispatchedAtUtc, DateTime? ArrivedAtUtc, DateTime? PouringStartedAtUtc,
    DateTime? PouringEndedAtUtc, DateTime? ReturnedAtUtc, DateTime? CancelledAtUtc, string? CancelReason);

// What the dispatcher and driver need to know about the site.
public record PourSurveyDto(
    Guid Id, SurveyResult Result, decimal? Latitude, decimal? Longitude, decimal? PipingMetres, string? PumpPosition,
    string? TruckConnectionPoint, string? LocationNote, List<Guid> ImageIds);

public record PourProductionDto(
    Guid PourId, Guid OrderId, Guid OrderLineId, Guid SiteId, decimal PlannedCft, DateTime PlannedStart, Guid? PumpId,
    DateTime? PumpDispatchedAtUtc, DateTime? PumpArrivedAtUtc, DateTime? PumpAssembledAtUtc,
    decimal ProducedCft, decimal RemainingCft, decimal DeliveredCft, PourSurveyDto? Survey, List<LoadDto> Loads);

public record ShiftProductionDto(
    Guid PlanId, Guid PlantId, DateOnly Date, Guid ShiftId, ShiftPlanStatus Status,
    decimal PlannedCft, decimal ProducedCft, decimal RemainingCft, decimal DeliveredCft, List<PourProductionDto> Pours);

public class ProduceRequest
{
    [Required]
    public Guid? TruckId { get; init; }

    // Leave out for the usual load. A smaller load needs a reason.
    [Range(0.01, 100000.0)]
    public decimal? QuantityCft { get; init; }

    [StringLength(300)]
    public string? Reason { get; init; }
}

public class OverProduceRequest : ProduceRequest
{
    [Required, StringLength(280, MinimumLength = 3)]
    public string? OverrideReason { get; init; }
}

public class TimedRequest
{
    // Leave out for now. It cannot be in the future.
    public DateTime? At { get; init; }
}

public class CancelLoadRequest
{
    [Required, StringLength(300, MinimumLength = 3)]
    public string? Reason { get; init; }
}

public class LoadStepRequest : TimedRequest
{
    [Required]
    public LoadStep? Step { get; init; }
}

public class PumpStepRequest : TimedRequest
{
    [Required]
    public PumpStep? Step { get; init; }
}

public class CapacityRequest
{
    // Leave out to use the company's truck-load setting.
    [Range(0.01, 1000.0)]
    public decimal? CapacityCft { get; init; }
}

[ApiController]
public class ProductionController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    // The operator's screen: the shift total, what is left to produce, and the loads.
    [HttpGet("api/shift-plans/{planId:guid}/operator")]
    [HasPermission(Permissions.BatchesManage)]
    public Task<ActionResult<ShiftProductionDto>> Operator(Guid planId, CancellationToken cancellationToken) =>
        BuildAsync(planId, cancellationToken);

    // Operations' dashboard: the same data with every timestamp.
    [HttpGet("api/shift-plans/{planId:guid}/dashboard")]
    [HasPermission(Permissions.DashboardsView)]
    public Task<ActionResult<ShiftProductionDto>> Dashboard(Guid planId, CancellationToken cancellationToken) =>
        BuildAsync(planId, cancellationToken);

    [HttpPut("api/vehicles/{id:guid}/capacity")]
    [HasPermission(Permissions.DeliveriesPlanApprove)]
    public async Task<IActionResult> SetCapacity(Guid id, CapacityRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null) return NotFound();

        if (request.CapacityCft is { } capacity && capacity != Math.Round(capacity, 2))
        {
            return Fail(StatusCodes.Status400BadRequest, "Capacity can have at most 2 decimals.");
        }

        vehicle.CapacityCft = request.CapacityCft;
        audit.Record(vehicle.TenantId, "vehicle.capacity_set", "Vehicle", vehicle.Id.ToString(), new { vehicle.CapacityCft });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { vehicleId = vehicle.Id, vehicle.CapacityCft });
    }

    // Produce a load for a pour. The quantity comes off the pour's remaining quantity.
    [HttpPost("api/pours/{id:guid}/loads")]
    [HasPermission(Permissions.BatchesManage)]
    public Task<ActionResult<LoadDto>> Produce(Guid id, ProduceRequest request, CancellationToken cancellationToken) =>
        ProduceAsync(id, request, null, cancellationToken);

    // Produce beyond the pour's planned quantity. The plant manager gives a reason.
    [HttpPost("api/pours/{id:guid}/loads/override")]
    [HasPermission(Permissions.DeliveriesPlanOverride)]
    public Task<ActionResult<LoadDto>> ProduceOverPlan(Guid id, OverProduceRequest request, CancellationToken cancellationToken) =>
        ProduceAsync(id, request, request.OverrideReason, cancellationToken);

    [HttpPost("api/loads/{id:guid}/dispatch")]
    [HasPermission(Permissions.BatchesManage)]
    public async Task<ActionResult<LoadDto>> Dispatch(Guid id, TimedRequest request, CancellationToken cancellationToken)
    {
        var load = await db.TruckLoads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (load is null) return NotFound();
        if (load.CancelledAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load was cancelled.");
        if (load.DispatchedAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load has already been dispatched.");

        if (Resolve(request.At) is not { } at) return Fail(StatusCodes.Status400BadRequest, "The time cannot be in the future.");
        if (at < load.ProducedAtUtc) return Fail(StatusCodes.Status400BadRequest, "A load cannot leave before it was produced.");

        load.DispatchedAtUtc = at;
        load.DispatchedBy = currentUser.UserId;
        audit.Record(load.TenantId, "load.dispatched", "TruckLoad", load.Id.ToString(), new { load.PourId, load.TruckId, at });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(load);
    }

    // A load that never reached the pour. Its quantity goes back to the pour.
    [HttpPost("api/loads/{id:guid}/cancel")]
    [HasPermission(Permissions.BatchesManage)]
    public async Task<ActionResult<LoadDto>> Cancel(Guid id, CancelLoadRequest request, CancellationToken cancellationToken)
    {
        var load = await db.TruckLoads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (load is null) return NotFound();
        if (load.CancelledAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load was already cancelled.");
        if (load.PouringStartedAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "Pouring has started, so the load cannot be cancelled.");

        load.CancelledAtUtc = DateTime.UtcNow;
        load.CancelReason = request.Reason!.Trim();
        audit.Record(load.TenantId, "load.cancelled", "TruckLoad", load.Id.ToString(), new { load.PourId, load.CancelReason });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(load);
    }

    // Truck steps recorded by operations. Sending a step again corrects its time.
    [HttpPost("api/loads/{id:guid}/steps")]
    [HasPermission(Permissions.DeliveriesTrack)]
    public async Task<ActionResult<LoadDto>> RecordStep(Guid id, LoadStepRequest request, CancellationToken cancellationToken)
    {
        var load = await db.TruckLoads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (load is null) return NotFound();
        if (load.CancelledAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load was cancelled.");
        if (load.DispatchedAtUtc is null) return Fail(StatusCodes.Status409Conflict, "Dispatch the load first.");

        if (Resolve(request.At) is not { } at) return Fail(StatusCodes.Status400BadRequest, "The time cannot be in the future.");

        DateTime?[] chain =
        [
            load.DispatchedAtUtc, load.ArrivedAtUtc, load.PouringStartedAtUtc, load.PouringEndedAtUtc, load.ReturnedAtUtc
        ];
        var step = request.Step!.Value;
        var index = step switch
        {
            LoadStep.Arrived => 1,
            LoadStep.PouringStarted => 2,
            LoadStep.PouringEnded => 3,
            _ => 4
        };
        if (CheckOrder(chain, index, at) is { } problem) return Fail(problem.Status, problem.Title);

        switch (step)
        {
            case LoadStep.Arrived: load.ArrivedAtUtc = at; break;
            case LoadStep.PouringStarted: load.PouringStartedAtUtc = at; break;
            case LoadStep.PouringEnded: load.PouringEndedAtUtc = at; break;
            default: load.ReturnedAtUtc = at; break;
        }

        audit.Record(load.TenantId, "load.step", "TruckLoad", load.Id.ToString(), new { load.PourId, Step = step.ToString(), at });
        await db.SaveChangesAsync(cancellationToken);

        // What was poured is what was delivered.
        if (step == LoadStep.PouringEnded)
        {
            await RecalculateDeliveredAsync(load.PourId, cancellationToken);
        }

        return ToDto(load);
    }

    // Pump steps recorded by operations. Sending a step again corrects its time.
    [HttpPost("api/pours/{id:guid}/pump-steps")]
    [HasPermission(Permissions.DeliveriesTrack)]
    public async Task<ActionResult<PourProductionDto>> RecordPumpStep(Guid id, PumpStepRequest request, CancellationToken cancellationToken)
    {
        var pour = await db.Pours.Include(p => p.ShiftPlan).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pour is null) return NotFound();
        if (pour.PumpId is null) return Fail(StatusCodes.Status409Conflict, "This pour has no pump.");
        if (pour.ShiftPlan.Status != ShiftPlanStatus.Approved)
        {
            return Fail(StatusCodes.Status409Conflict, "The shift plan has to be approved first.");
        }

        if (Resolve(request.At) is not { } at) return Fail(StatusCodes.Status400BadRequest, "The time cannot be in the future.");

        DateTime?[] chain = [pour.PumpDispatchedAtUtc, pour.PumpArrivedAtUtc, pour.PumpAssembledAtUtc];
        var step = request.Step!.Value;
        var index = (int)step;
        if (CheckOrder(chain, index, at) is { } problem) return Fail(problem.Status, problem.Title);

        switch (step)
        {
            case PumpStep.Dispatched: pour.PumpDispatchedAtUtc = at; break;
            case PumpStep.Arrived: pour.PumpArrivedAtUtc = at; break;
            default: pour.PumpAssembledAtUtc = at; break;
        }

        audit.Record(pour.TenantId, "pour.pump_step", "Pour", pour.Id.ToString(), new { Step = step.ToString(), at });
        await db.SaveChangesAsync(cancellationToken);

        var view = await BuildAsync(pour.ShiftPlanId, cancellationToken);
        return view.Value!.Pours.Single(p => p.PourId == id);
    }

    private async Task<ActionResult<LoadDto>> ProduceAsync(
        Guid pourId, ProduceRequest request, string? overrideReason, CancellationToken cancellationToken)
    {
        var pour = await db.Pours.AsNoTracking().Where(p => p.Id == pourId).Select(p => new
        {
            p.TenantId,
            p.PlannedCft,
            p.PumpId,
            PlanStatus = p.ShiftPlan.Status,
            OrderStatus = p.OrderLine.Order.Status
        }).FirstOrDefaultAsync(cancellationToken);
        if (pour is null) return NotFound();

        if (pour.PlanStatus != ShiftPlanStatus.Approved)
        {
            return Fail(StatusCodes.Status409Conflict, "Only a pour in an approved shift plan can be produced.");
        }

        if (pour.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.InProgress))
        {
            return Fail(StatusCodes.Status409Conflict, "The order is not confirmed or running, so nothing can be produced for it.");
        }

        var truckId = request.TruckId!.Value;
        var truck = await db.Vehicles.AsNoTracking().Where(v => v.Id == truckId)
            .Select(v => new { v.CapacityCft }).FirstOrDefaultAsync(cancellationToken);
        if (truck is null || truckId == pour.PumpId)
        {
            return Fail(StatusCodes.Status400BadRequest, "Choose a truck. The pump cannot carry a load.");
        }

        if (request.QuantityCft is { } requested && requested != Math.Round(requested, 2))
        {
            return Fail(StatusCodes.Status400BadRequest, "Quantity can have at most 2 decimals.");
        }

        if (await db.TruckLoads.AnyAsync(
                l => l.TruckId == truckId && l.CancelledAtUtc == null && l.ReturnedAtUtc == null, cancellationToken))
        {
            return Fail(StatusCodes.Status409Conflict, "This truck is still out on another load.");
        }

        var produced = await db.TruckLoads.Where(l => l.PourId == pourId && l.CancelledAtUtc == null)
            .SumAsync(l => (decimal?)l.QuantityCft, cancellationToken) ?? 0m;
        var remaining = pour.PlannedCft - produced;

        var companyLoad = await db.Tenants.Where(t => t.Id == pour.TenantId)
            .Select(t => t.TruckLoadCft).FirstAsync(cancellationToken);
        var size = truck.CapacityCft ?? companyLoad;

        decimal quantity;
        var note = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        if (overrideReason is null)
        {
            if (remaining <= 0)
            {
                return Fail(StatusCodes.Status409Conflict, "This pour has already been produced in full.");
            }

            // The usual load, or whatever is left if that is less.
            var usual = Math.Min(size, remaining);
            quantity = request.QuantityCft ?? usual;
            if (quantity > size)
            {
                return Fail(StatusCodes.Status400BadRequest, $"A load cannot be more than the truck's load size of {size} CFT.");
            }

            if (quantity > remaining)
            {
                return Fail(StatusCodes.Status409Conflict, $"Only {remaining} CFT remain for this pour.");
            }

            if (quantity != usual && note is null)
            {
                return Fail(StatusCodes.Status400BadRequest, "Give a reason for a load smaller than usual.");
            }
        }
        else
        {
            quantity = request.QuantityCft ?? size;
            if (quantity > size)
            {
                return Fail(StatusCodes.Status400BadRequest, $"A load cannot be more than the truck's load size of {size} CFT.");
            }

            note = $"Over plan: {overrideReason.Trim()}";
        }

        var load = new TruckLoad
        {
            TenantId = pour.TenantId,
            PourId = pourId,
            TruckId = truckId,
            Number = await db.TruckLoads.CountAsync(l => l.PourId == pourId, cancellationToken) + 1,
            QuantityCft = quantity,
            QuantityReason = note,
            ProducedBy = currentUser.UserId
        };

        db.TruckLoads.Add(load);
        audit.Record(load.TenantId, overrideReason is null ? "load.produced" : "load.produced_over_plan", "TruckLoad",
            load.Id.ToString(), new { load.PourId, load.TruckId, load.Number, load.QuantityCft, load.QuantityReason });
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/loads/{load.Id}", ToDto(load));
    }

    // The order line's delivered quantity is what its loads poured. It starts the order when the first concrete is poured.
    private async Task RecalculateDeliveredAsync(Guid pourId, CancellationToken cancellationToken)
    {
        var lineId = await db.Pours.Where(p => p.Id == pourId).Select(p => p.OrderLineId).FirstAsync(cancellationToken);
        var line = await db.OrderLines.Include(l => l.Order).FirstAsync(l => l.Id == lineId, cancellationToken);
        if (line.Order.Status == OrderStatus.Completed) return;

        var total = await db.TruckLoads
            .Where(l => l.CancelledAtUtc == null && l.PouringEndedAtUtc != null && l.Pour.OrderLineId == lineId)
            .SumAsync(l => (decimal?)l.QuantityCft, cancellationToken) ?? 0m;

        if (line.DeliveredCft != total)
        {
            line.DeliveredCft = total;

            // A new quantity means the difference has to be looked at again.
            line.BilledCft = null;
            line.BillingBasis = null;
            line.BillingNote = null;
            line.BillingResolvedBy = null;
            //line.BillingResolvedAt = null;
        }

        if (line.Order.Status == OrderStatus.Confirmed && total > 0)
        {
            line.Order.Status = OrderStatus.InProgress;
        }

        audit.Record(line.Order.TenantId, "order.delivered_from_loads", "Order", line.Order.Id.ToString(), new { lineId, total });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ActionResult<ShiftProductionDto>> BuildAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.ShiftPlans.AsNoTracking().Where(p => p.Id == planId)
            .Select(p => new { p.Id, p.PlantId, p.Date, p.ShiftId, p.Status }).FirstOrDefaultAsync(cancellationToken);
        if (plan is null) return NotFound();

        var pours = await db.Pours.AsNoTracking().Where(p => p.ShiftPlanId == planId).OrderBy(p => p.PlannedStart)
            .Select(p => new
            {
                p.Id,
                OrderId = p.OrderLine.Order.Id,
                p.OrderLineId,
                p.OrderLine.Order.SiteId,
                Tagged = p.OrderLine.Order.SiteSurveyId,
                p.PlannedCft,
                p.PlannedStart,
                p.PumpId,
                p.PumpDispatchedAtUtc,
                p.PumpArrivedAtUtc,
                p.PumpAssembledAtUtc
            }).ToListAsync(cancellationToken);

        var pourIds = pours.Select(p => p.Id).ToList();
        var loads = await db.TruckLoads.AsNoTracking().Where(l => pourIds.Contains(l.PourId))
            .OrderBy(l => l.Number).ToListAsync(cancellationToken);

        // Each pour shows the survey its order tagged, or else the site's latest.
        var tagged = pours.Where(p => p.Tagged != null).Select(p => p.Tagged!.Value).Distinct().ToList();
        var siteIds = pours.Select(p => p.SiteId).Distinct().ToList();
        var surveys = await db.SiteSurveys.AsNoTracking()
            .Where(s => tagged.Contains(s.Id) || siteIds.Contains(s.SiteId))
            .Select(s => new
            {
                s.Id,
                s.SiteId,
                s.SurveyedOn,
                s.CreatedAtUtc,
                s.Result,
                s.Latitude,
                s.Longitude,
                s.PipingMetres,
                s.PumpPosition,
                s.TruckConnectionPoint,
                s.LocationNote
            }).ToListAsync(cancellationToken);
        var surveyIds = surveys.Select(s => s.Id).ToList();
        var images = await db.SiteSurveyImages.AsNoTracking().Where(i => surveyIds.Contains(i.SurveyId))
            .OrderBy(i => i.CreatedAtUtc).Select(i => new { i.SurveyId, i.Id }).ToListAsync(cancellationToken);

        var items = pours.Select(p =>
        {
            var mine = loads.Where(l => l.PourId == p.Id).ToList();
            var live = mine.Where(l => l.CancelledAtUtc is null).ToList();
            var producedCft = live.Sum(l => l.QuantityCft);
            var deliveredCft = live.Where(l => l.PouringEndedAtUtc is not null).Sum(l => l.QuantityCft);

            var survey = p.Tagged is { } id
                ? surveys.FirstOrDefault(s => s.Id == id)
                : surveys.Where(s => s.SiteId == p.SiteId)
                    .OrderByDescending(s => s.SurveyedOn).ThenByDescending(s => s.CreatedAtUtc).FirstOrDefault();

            return new PourProductionDto(
                p.Id, p.OrderId, p.OrderLineId, p.SiteId, p.PlannedCft, p.PlannedStart, p.PumpId,
                p.PumpDispatchedAtUtc, p.PumpArrivedAtUtc, p.PumpAssembledAtUtc,
                producedCft, p.PlannedCft - producedCft, deliveredCft,
                survey is null ? null : new PourSurveyDto(
                    survey.Id, survey.Result, survey.Latitude, survey.Longitude, survey.PipingMetres, survey.PumpPosition,
                    survey.TruckConnectionPoint, survey.LocationNote,
                    images.Where(i => i.SurveyId == survey.Id).Select(i => i.Id).ToList()),
                mine.Select(ToDto).ToList());
        }).ToList();

        var planned = items.Sum(i => i.PlannedCft);
        var producedTotal = items.Sum(i => i.ProducedCft);
        return new ShiftProductionDto(
            plan.Id, plan.PlantId, plan.Date, plan.ShiftId, plan.Status,
            planned, producedTotal, planned - producedTotal, items.Sum(i => i.DeliveredCft), items);
    }

    // A time must not be in the future. Without one, it is now.
    private static DateTime? Resolve(DateTime? at)
    {
        var now = DateTime.UtcNow;
        var time = at is null
            ? now
            : at.Value.Kind == DateTimeKind.Local ? at.Value.ToUniversalTime() : DateTime.SpecifyKind(at.Value, DateTimeKind.Utc);
        return time > now.AddMinutes(5) ? null : time;
    }

    // A step cannot be earlier than the one before it, or later than the one after it. The one before must exist.
    private static (int Status, string Title)? CheckOrder(DateTime?[] chain, int index, DateTime at)
    {
        if (index > 0)
        {
            var before = chain[index - 1];
            if (before is null) return (StatusCodes.Status409Conflict, "Record the earlier step first.");
            if (at < before) return (StatusCodes.Status400BadRequest, "That time is before the earlier step.");
        }

        if (index + 1 < chain.Length && chain[index + 1] is { } after && at > after)
        {
            return (StatusCodes.Status400BadRequest, "That time is after the next step.");
        }

        return null;
    }

    private ObjectResult Fail(int status, string title) => Problem(title: title, statusCode: status);

    private static LoadDto ToDto(TruckLoad l) => new(
        l.Id, l.PourId, l.Number, l.TruckId, l.QuantityCft, l.QuantityReason,
        l.CancelledAtUtc is not null ? "Cancelled"
        : l.ReturnedAtUtc is not null ? "Returned"
        : l.PouringEndedAtUtc is not null ? "Poured"
        : l.PouringStartedAtUtc is not null ? "Pouring"
        : l.ArrivedAtUtc is not null ? "AtSite"
        : l.DispatchedAtUtc is not null ? "OnTheRoad"
        : "Produced",
        l.ProducedAtUtc, l.DispatchedAtUtc, l.ArrivedAtUtc, l.PouringStartedAtUtc, l.PouringEndedAtUtc,
        l.ReturnedAtUtc, l.CancelledAtUtc, l.CancelReason);
}