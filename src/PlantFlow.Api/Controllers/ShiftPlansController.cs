using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record ShiftPlanSummaryDto(
    Guid Id, Guid PlantId, string PlantCode, DateOnly Date, Guid ShiftId, string ShiftName,
    TimeOnly ShiftStart, TimeOnly ShiftEnd, ShiftPlanStatus Status, int PourCount, decimal TotalPlannedCft,
    string? ReturnReason, DateTime? SubmittedAtUtc, DateTime? ApprovedAtUtc);

public record PourDto(
    Guid Id, Guid OrderId, string OrderNumber, string CustomerName, string SiteName, Guid OrderLineId,
    string ProductCode, PourMethod PourMethod, decimal PlannedCft, DateTime PlannedStart,
    Guid? PumpId, string? PumpCode, Guid? OperatorId, string? OperatorName, List<string> Crew, string? CrewNotes,
    DateTime? PumpLeaveAt, DateTime FirstTruckLeaveAt, int EstimatedTruckLoads, string? RestOverrideReason, string? Notes);

public record ShiftPlanDetailDto(ShiftPlanSummaryDto Plan, List<PourDto> Pours);

public class ShiftPlanRequest
{
    [Required]
    public Guid? PlantId { get; init; }

    // The service day the shift starts on.
    [Required]
    public DateOnly? Date { get; init; }

    [Required]
    public Guid? ShiftId { get; init; }
}

public class PourRequest
{
    [Required]
    public Guid? OrderLineId { get; init; }

    // At most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal PlannedCft { get; init; }

    // The casting start, in plant time, whole minutes. It must fall inside the shift.
    [Required]
    public TimeOnly? StartTime { get; init; }

    // Required for a line poured by pump, not allowed for any other line.
    public Guid? PumpId { get; init; }

    // Leave it out to use the pump's default driver.
    public Guid? OperatorId { get; init; }

    [StringLength(500)]
    public string? CrewNotes { get; init; }

    // Leave these out to use the calculated times. Plant time.
    public DateTime? PumpLeaveAt { get; init; }
    public DateTime? FirstTruckLeaveAt { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public class OverridePourRequest
{
    // Why the pump is used before it has rested.
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";

    [Required]
    public PourRequest Pour { get; init; } = null!;
}

public class PlanReasonRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
public class ShiftPlansController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, PourPlanner planner) : ControllerBase
{
    private static readonly Expression<Func<ShiftPlan, ShiftPlanSummaryDto>> ToSummary = p => new ShiftPlanSummaryDto(
        p.Id, p.PlantId, p.Plant.Code, p.Date, p.ShiftId, p.Shift.Name, p.Shift.StartTime, p.Shift.EndTime, p.Status,
        p.Pours.Count(), p.Pours.Sum(x => (decimal?)x.PlannedCft) ?? 0m, p.ReturnReason, p.SubmittedAtUtc, p.ApprovedAtUtc);

    // ?plantId= ?status= ?from= ?to= (dates). Newest day first, shifts in start order.
    [HttpGet("api/shift-plans")]
    [HasPermission(Permissions.DeliveriesTrack)]
    public async Task<ActionResult<PagedResult<ShiftPlanSummaryDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? plantId, [FromQuery] ShiftPlanStatus? status,
        [FromQuery(Name = "from")] DateOnly? dateFrom, [FromQuery(Name = "to")] DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var query = db.ShiftPlans.AsNoTracking();

        if (plantId is not null) query = query.Where(p => p.PlantId == plantId);
        if (status is not null) query = query.Where(p => p.Status == status);
        if (dateFrom is not null) query = query.Where(p => p.Date >= dateFrom);
        if (dateTo is not null) query = query.Where(p => p.Date <= dateTo);

        query = query.OrderByDescending(p => p.Date).ThenBy(p => p.Shift.StartTime);
        return await query.Select(ToSummary).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("api/shift-plans/{id:guid}")]
    [HasPermission(Permissions.DeliveriesTrack)]
    public async Task<ActionResult<ShiftPlanDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await LoadPlanAsync(id, cancellationToken);
        if (detail is null) return NotFound();
        return detail;
    }

    [HttpPost("api/shift-plans")]
    [HasPermission(Permissions.DeliveriesPlan)]
    public async Task<ActionResult<ShiftPlanDetailDto>> Create(ShiftPlanRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(p => p.Id == request.PlantId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.PlantShifts.AnyAsync(
                s => s.Id == request.ShiftId && s.PlantId == request.PlantId && s.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active shift of this plant.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.ShiftPlans.AnyAsync(
                p => p.PlantId == request.PlantId && p.Date == request.Date && p.ShiftId == request.ShiftId, cancellationToken))
        {
            return Problem(title: "There is already a plan for this plant, date and shift.", statusCode: StatusCodes.Status409Conflict);
        }

        var plan = new ShiftPlan
        {
            TenantId = currentUser.TenantId!.Value,
            PlantId = request.PlantId!.Value,
            Date = request.Date!.Value,
            ShiftId = request.ShiftId!.Value
        };

        db.ShiftPlans.Add(plan);
        audit.Record(plan.TenantId, "shiftplan.created", "ShiftPlan", plan.Id.ToString(),
            new { plan.PlantId, plan.Date, plan.ShiftId });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Problem(title: "There is already a plan for this plant, date and shift.", statusCode: StatusCodes.Status409Conflict);
        }

        var detail = await LoadPlanAsync(plan.Id, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, detail);
    }

    [HttpPost("api/shift-plans/{id:guid}/submit")]
    [HasPermission(Permissions.DeliveriesPlan)]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, ShiftPlanStatus.Draft, ShiftPlanStatus.Submitted, "shiftplan.submitted", null,
            plan => plan.Pours.Count == 0 ? "Plan at least one pour before submitting." : null,
            plan =>
            {
                plan.SubmittedBy = currentUser.UserId;
                plan.SubmittedAtUtc = DateTime.UtcNow;
                plan.ReturnReason = null;
            }, cancellationToken);

    [HttpPost("api/shift-plans/{id:guid}/approve")]
    [HasPermission(Permissions.DeliveriesPlanApprove)]
    public Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, ShiftPlanStatus.Submitted, ShiftPlanStatus.Approved, "shiftplan.approved", null, null,
            plan =>
            {
                plan.ApprovedBy = currentUser.UserId;
                plan.ApprovedAtUtc = DateTime.UtcNow;
            }, cancellationToken);

    // The plant head sends a submitted plan back to the planners.
    [HttpPost("api/shift-plans/{id:guid}/reject")]
    [HasPermission(Permissions.DeliveriesPlanApprove)]
    public Task<IActionResult> Reject(Guid id, PlanReasonRequest request, CancellationToken cancellationToken) =>
        MoveAsync(id, ShiftPlanStatus.Submitted, ShiftPlanStatus.Draft, "shiftplan.rejected", request.Reason.Trim(), null,
            plan =>
            {
                plan.ReturnReason = request.Reason.Trim();
                plan.SubmittedBy = null;
                plan.SubmittedAtUtc = null;
            }, cancellationToken);

    // The plant head takes an approved plan back to a draft so it can change.
    [HttpPost("api/shift-plans/{id:guid}/reopen")]
    [HasPermission(Permissions.DeliveriesPlanApprove)]
    public Task<IActionResult> Reopen(Guid id, PlanReasonRequest request, CancellationToken cancellationToken) =>
        MoveAsync(id, ShiftPlanStatus.Approved, ShiftPlanStatus.Draft, "shiftplan.reopened", request.Reason.Trim(), null,
            plan =>
            {
                plan.ReturnReason = request.Reason.Trim();
                plan.SubmittedBy = null;
                plan.SubmittedAtUtc = null;
                plan.ApprovedBy = null;
                plan.ApprovedAtUtc = null;
            }, cancellationToken);

    [HttpPost("api/shift-plans/{planId:guid}/pours")]
    [HasPermission(Permissions.DeliveriesPlan)]
    public Task<IActionResult> AddPour(Guid planId, PourRequest request, CancellationToken cancellationToken) =>
        SavePourAsync(planId, null, request, null, cancellationToken);

    // The same, but a plant manager accepts a pump that has not rested long enough. The reason is kept on the pour.
    [HttpPost("api/shift-plans/{planId:guid}/pours/override")]
    [HasPermission(Permissions.DeliveriesPlanOverride)]
    public Task<IActionResult> AddPourOverride(Guid planId, OverridePourRequest request, CancellationToken cancellationToken) =>
        SavePourAsync(planId, null, request.Pour, request.Reason.Trim(), cancellationToken);

    [HttpPut("api/pours/{id:guid}")]
    [HasPermission(Permissions.DeliveriesPlan)]
    public Task<IActionResult> UpdatePour(Guid id, PourRequest request, CancellationToken cancellationToken) =>
        SavePourAsync(null, id, request, null, cancellationToken);

    [HttpPut("api/pours/{id:guid}/override")]
    [HasPermission(Permissions.DeliveriesPlanOverride)]
    public Task<IActionResult> UpdatePourOverride(Guid id, OverridePourRequest request, CancellationToken cancellationToken) =>
        SavePourAsync(null, id, request.Pour, request.Reason.Trim(), cancellationToken);

    [HttpDelete("api/pours/{id:guid}")]
    [HasPermission(Permissions.DeliveriesPlan)]
    public async Task<IActionResult> RemovePour(Guid id, CancellationToken cancellationToken)
    {
        var pour = await db.Pours.Include(p => p.ShiftPlan).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pour is null) return NotFound();

        if (pour.ShiftPlan.Status != ShiftPlanStatus.Draft)
        {
            return Problem(title: $"Only a draft shift plan can be changed. This plan is {pour.ShiftPlan.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.Pours.Remove(pour);
        audit.Record(pour.TenantId, "pour.removed", "ShiftPlan", pour.ShiftPlanId.ToString(),
            new { pour.OrderLineId, pour.PlannedCft });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> MoveAsync(
        Guid id, ShiftPlanStatus from, ShiftPlanStatus to, string action, string? reason,
        Func<ShiftPlan, string?>? check, Action<ShiftPlan> apply, CancellationToken cancellationToken)
    {
        var plan = await db.ShiftPlans.Include(p => p.Pours).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null) return NotFound();

        if (plan.Status != from)
        {
            return Problem(title: $"This shift plan is {plan.Status}, not {from}.", statusCode: StatusCodes.Status409Conflict);
        }

        var problem = check?.Invoke(plan);
        if (problem is not null) return Problem(title: problem, statusCode: StatusCodes.Status409Conflict);

        plan.Status = to;
        apply(plan);

        audit.Record(plan.TenantId, action, "ShiftPlan", plan.Id.ToString(),
            new { plan.PlantId, plan.Date, plan.ShiftId, From = from.ToString(), To = to.ToString(), Reason = reason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Adds a pour to a draft plan, or updates one. overrideReason is only set by the override endpoints.
    private async Task<IActionResult> SavePourAsync(
        Guid? planId, Guid? pourId, PourRequest r, string? overrideReason, CancellationToken cancellationToken)
    {
        IActionResult Bad(string title) => Problem(title: title, statusCode: StatusCodes.Status400BadRequest);
        IActionResult Clash(string title) => Problem(title: title, statusCode: StatusCodes.Status409Conflict);

        Pour? pour = null;
        ShiftPlan? plan;
        if (pourId is { } existingId)
        {
            pour = await db.Pours.Include(p => p.ShiftPlan).FirstOrDefaultAsync(p => p.Id == existingId, cancellationToken);
            plan = pour?.ShiftPlan;
        }
        else
        {
            plan = await db.ShiftPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        }

        if (plan is null) return NotFound();

        if (plan.Status != ShiftPlanStatus.Draft)
        {
            return Clash($"Only a draft shift plan can be changed. This plan is {plan.Status}.");
        }

        var line = await db.OrderLines.AsNoTracking().Where(l => l.Id == r.OrderLineId)
            .Select(l => new
            {
                l.Id,
                l.QuantityCft,
                l.PourMethod,
                OrderStatus = l.Order.Status,
                OrderPlantId = l.Order.PlantId,
                l.Order.SiteId,
                l.Order.DistanceKm
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (line is null) return Bad("Choose an order line of this company.");

        if (line.OrderStatus is not (OrderStatus.Confirmed or OrderStatus.InProgress))
        {
            return Bad($"Only a confirmed or running order can be planned. This order is {line.OrderStatus}.");
        }

        if (line.OrderPlantId != plan.PlantId) return Bad("This order is for another plant.");

        if (r.PlannedCft != Math.Round(r.PlannedCft, 2)) return Bad("The quantity can have at most 2 decimal places.");

        var startTime = r.StartTime!.Value;
        if (startTime.Ticks % TimeSpan.TicksPerMinute != 0) return Bad("The start time must be in whole minutes.");

        var shift = await db.PlantShifts.AsNoTracking().FirstAsync(s => s.Id == plan.ShiftId, cancellationToken);
        var start = PourPlanner.CastingStart(shift, plan.Date, startTime);
        if (start is null)
        {
            return Bad($"The start time must fall inside the {shift.Name} shift ({shift.StartTime:HH:mm} to {shift.EndTime:HH:mm}).");
        }

        var byPump = line.PourMethod == PourMethod.Pump;
        if (byPump && r.PumpId is null) return Bad("Choose the pump for this pour.");
        if (!byPump && r.PumpId is not null) return Bad("This line is not poured by pump, so it cannot have a pump.");

        Vehicle? pump = null;
        if (r.PumpId is { } pumpId)
        {
            pump = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == pumpId && v.Type == VehicleType.Pump, cancellationToken);
            if (pump is null || pump.PlantId != plan.PlantId || pump.Status is VehicleStatus.Maintenance or VehicleStatus.Retired)
            {
                return Bad("Choose an available pump of this plant.");
            }
        }

        if (r.OperatorId is { } operatorId && pour?.OperatorId != operatorId
            && !await db.Drivers.AnyAsync(d => d.Id == operatorId && d.Status == ActiveStatus.Active, cancellationToken))
        {
            return Bad("Choose an active driver of this company as the operator.");
        }

        if ((r.PumpLeaveAt is not null && pump is null)
            || (r.PumpLeaveAt is { } leave && leave > start)
            || (r.FirstTruckLeaveAt is { } truck && truck > start))
        {
            return Bad("A leave time needs a pump and must be before the casting start.");
        }

        var planned = await db.Pours.Where(p => p.OrderLineId == line.Id && p.Id != pourId)
            .SumAsync(p => (decimal?)p.PlannedCft, cancellationToken) ?? 0m;
        if (planned + r.PlannedCft > line.QuantityCft)
        {
            return Clash($"{planned} of {line.QuantityCft} CFT is already planned, so {r.PlannedCft} more is too much.");
        }

        var settings = await db.Tenants.AsNoTracking().Where(t => t.Id == plan.TenantId)
            .Select(t => new { t.RequireSurveyBeforePlanning, t.PumpRestShifts, t.AverageSpeedKmh, t.PumpAssemblyMinutes, t.TruckLoadCft })
            .FirstAsync(cancellationToken);

        var needsSurvey = settings.RequireSurveyBeforePlanning == SurveyRequirement.All
                          || (settings.RequireSurveyBeforePlanning == SurveyRequirement.PumpJobs && byPump);
        if (needsSurvey)
        {
            var latest = await db.SiteSurveys.AsNoTracking().Where(s => s.SiteId == line.SiteId)
                .OrderByDescending(s => s.SurveyedOn).ThenByDescending(s => s.CreatedAtUtc)
                .Select(s => (SurveyResult?)s.Result).FirstOrDefaultAsync(cancellationToken);
            if (latest != SurveyResult.Ok)
            {
                return Clash("The site needs a survey with an OK result before this pour can be planned.");
            }
        }

        var restOverridden = false;
        if (pump is not null)
        {
            var conflict = await planner.PumpConflictsAsync(plan, pump.Id, pourId, settings.PumpRestShifts, cancellationToken);
            if (conflict.SameShift) return Clash("This pump is already planned for another pour in this shift.");

            if (conflict.RestMessage is not null)
            {
                if (overrideReason is null) return Clash(conflict.RestMessage + " A plant manager can override this with a reason.");
                restOverridden = true;
            }
        }

        var assembly = pump is null ? (int?)null : pump.AssemblyMinutes ?? settings.PumpAssemblyMinutes;
        var times = PourPlanner.Calculate(start.Value, line.DistanceKm, settings.AverageSpeedKmh, r.PlannedCft, settings.TruckLoadCft, assembly);

        if (pour is null)
        {
            pour = new Pour { TenantId = plan.TenantId, ShiftPlanId = plan.Id, OrderLineId = line.Id };
            db.Pours.Add(pour);
        }

        pour.PlannedCft = r.PlannedCft;
        pour.PlannedStart = start.Value;
        pour.PumpId = pump?.Id;
        pour.OperatorId = r.OperatorId;
        pour.CrewNotes = r.CrewNotes?.Trim();
        pour.PumpLeaveAt = r.PumpLeaveAt ?? times.PumpLeaveAt;
        pour.FirstTruckLeaveAt = r.FirstTruckLeaveAt ?? times.FirstTruckLeaveAt;
        pour.EstimatedTruckLoads = times.TruckLoads;
        pour.RestOverrideReason = restOverridden ? overrideReason : null;
        pour.Notes = r.Notes?.Trim();

        var isNew = pourId is null;
        audit.Record(plan.TenantId, restOverridden ? "pour.rest_overridden" : isNew ? "pour.planned" : "pour.updated",
            "ShiftPlan", plan.Id.ToString(),
            new { pour.OrderLineId, pour.PlannedCft, pour.PlannedStart, pour.PumpId, pour.RestOverrideReason });
        await db.SaveChangesAsync(cancellationToken);

        if (!isNew) return NoContent();

        var dto = (await LoadPoursAsync(null, pour.Id, cancellationToken)).Single();
        return Created($"api/shift-plans/{plan.Id}", dto);
    }

    private async Task<ShiftPlanDetailDto?> LoadPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        var summary = await db.ShiftPlans.AsNoTracking().Where(p => p.Id == id).Select(ToSummary)
            .FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        return new ShiftPlanDetailDto(summary, await LoadPoursAsync(id, null, cancellationToken));
    }

    // The crew is the pump's named labour team. The operator is the pour's own or the pump's default driver.
    private async Task<List<PourDto>> LoadPoursAsync(Guid? planId, Guid? pourId, CancellationToken cancellationToken)
    {
        var rows = await db.Pours.AsNoTracking()
            .Where(p => (planId == null || p.ShiftPlanId == planId) && (pourId == null || p.Id == pourId))
            .OrderBy(p => p.PlannedStart).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                OrderId = p.OrderLine.OrderId,
                OrderNumber = p.OrderLine.Order.Number,
                CustomerName = p.OrderLine.Order.Customer.Name,
                SiteName = p.OrderLine.Order.Site.Name,
                p.OrderLineId,
                ProductCode = p.OrderLine.Product.Code,
                p.OrderLine.PourMethod,
                p.PlannedCft,
                p.PlannedStart,
                p.PumpId,
                PumpCode = p.Pump != null ? p.Pump.Code : null,
                OperatorId = p.OperatorId ?? (p.Pump != null ? p.Pump.DefaultDriverId : (Guid?)null),
                OperatorName = p.Operator != null
                    ? p.Operator.Name
                    : (p.Pump != null && p.Pump.DefaultDriver != null ? p.Pump.DefaultDriver.Name : null),
                p.CrewNotes,
                p.PumpLeaveAt,
                p.FirstTruckLeaveAt,
                p.EstimatedTruckLoads,
                p.RestOverrideReason,
                p.Notes
            })
            .ToListAsync(cancellationToken);

        var pumpIds = rows.Where(r => r.PumpId != null).Select(r => r.PumpId!.Value).Distinct().ToList();
        var crew = await db.PumpCrewMembers.AsNoTracking().Where(c => pumpIds.Contains(c.VehicleId))
            .OrderBy(c => c.Name).Select(c => new { c.VehicleId, c.Name }).ToListAsync(cancellationToken);

        return rows.Select(r => new PourDto(
            r.Id, r.OrderId, r.OrderNumber, r.CustomerName, r.SiteName, r.OrderLineId, r.ProductCode, r.PourMethod,
            r.PlannedCft, r.PlannedStart, r.PumpId, r.PumpCode, r.OperatorId, r.OperatorName,
            r.PumpId is null ? [] : crew.Where(c => c.VehicleId == r.PumpId).Select(c => c.Name).ToList(),
            r.CrewNotes, r.PumpLeaveAt, r.FirstTruckLeaveAt, r.EstimatedTruckLoads, r.RestOverrideReason, r.Notes)).ToList();
    }
}