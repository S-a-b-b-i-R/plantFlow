using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// ---- Live operations ----

// A pour step against its plan. Late means more than the company's threshold behind.
// Delay is null until the step is due. Early steps have a negative delay.
public record StepDelayDto(DateTime PlannedUtc, DateTime? ActualUtc, int? DelayMinutes, bool Late);

public record LoadCountsDto(int Waiting, int OnTheRoad, int AtSite, int Pouring, int Returning, int Done);

// PlannedStart is plant-local, as planned. The steps carry UTC times.
public record LivePourDto(
    Guid PourId, Guid OrderId, Guid? PumpId, decimal PlannedCft, DateTime PlannedStart,
    decimal ProducedCft, decimal RemainingCft, decimal DeliveredCft, LoadCountsDto Loads,
    DateTime? PumpArrivedAtUtc, StepDelayDto? PumpDispatch, StepDelayDto? PumpReady, StepDelayDto? FirstTruck,
    StepDelayDto? PourStart, bool Late);

public record LivePlanDto(
    Guid PlanId, Guid PlantId, string PlantName, Guid ShiftId, string ShiftName, ShiftPlanStatus Status,
    decimal PlannedCft, decimal ProducedCft, decimal RemainingCft, decimal DeliveredCft,
    LoadCountsDto Loads, int LatePours, List<LivePourDto> Pours);

public record LiveDashboardDto(
    DateOnly Date, int LateThresholdMinutes, DateTime AsOfUtc, decimal PlannedCft, decimal ProducedCft,
    decimal DeliveredCft, LoadCountsDto Loads, int LatePours, List<LivePlanDto> Plans);

// ---- Production and delivery ----

public record ProductionFiguresDto(
    decimal PlannedCft, decimal ProducedCft, decimal DeliveredCft, decimal ReturnedCft, decimal DumpedCft,
    int Pours, int Loads, int RejectedLoads);

public record PlantFiguresDto(Guid PlantId, string PlantName, ProductionFiguresDto Figures);

public record DayFiguresDto(DateOnly Date, ProductionFiguresDto Figures);

public record ProductFiguresDto(Guid ProductId, string ProductName, ProductionFiguresDto Figures);

public record ProductionDashboardDto(
    DateOnly From, DateOnly To, ProductionFiguresDto Total, List<PlantFiguresDto> ByPlant,
    List<DayFiguresDto> ByDay, List<ProductFiguresDto> ByProduct);

// ---- Orders and billing ----

public record OrderStatusCountDto(OrderStatus Status, int Orders, decimal ConfirmedValue);

public record InvoicedDto(int Orders, decimal Subtotal, decimal Vat, decimal Total);

public record PlantOrdersDto(Guid PlantId, string PlantName, List<OrderStatusCountDto> ByStatus, InvoicedDto Invoiced);

public record OrdersDashboardDto(
    DateOnly From, DateOnly To, List<OrderStatusCountDto> ByStatus, InvoicedDto Invoiced, List<PlantOrdersDto> ByPlant);

[ApiController]
public class DashboardsController(PlantFlowDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private const int MaxDays = 366;

    // Today's shifts, or the shifts of another date. Times in the steps are UTC.
    [HttpGet("api/dashboards/live")]
    [HasPermission(Permissions.DashboardsView)]
    public async Task<ActionResult<LiveDashboardDto>> Live(Guid? plantId, DateOnly? date, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var offset = TimeSpan.FromMinutes(settings.UtcOffsetMinutes);
        var nowUtc = DateTime.UtcNow;
        var day = date ?? DateOnly.FromDateTime(nowUtc + offset);

        var plans = await db.ShiftPlans.AsNoTracking()
            .Where(p => p.Date == day && (plantId == null || p.PlantId == plantId))
            .Select(p => new { p.Id, p.PlantId, PlantName = p.Plant.Name, p.ShiftId, ShiftName = p.Shift.Name, p.Status })
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(p => p.Id).ToList();

        var pours = await db.Pours.AsNoTracking().Where(p => planIds.Contains(p.ShiftPlanId))
            .Select(p => new
            {
                p.Id,
                p.ShiftPlanId,
                OrderId = p.OrderLine.Order.Id,
                p.PumpId,
                p.PlannedCft,
                p.PlannedStart,
                p.PumpLeaveAt,
                p.FirstTruckLeaveAt,
                p.PumpDispatchedAtUtc,
                p.PumpArrivedAtUtc,
                p.PumpAssembledAtUtc
            }).ToListAsync(cancellationToken);
        var pourIds = pours.Select(p => p.Id).ToList();

        var loads = await db.TruckLoads.AsNoTracking().Where(l => pourIds.Contains(l.PourId)).ToListAsync(cancellationToken);
        var loadIds = loads.Select(l => l.Id).ToList();
        var records = await db.ReturnedConcretes.AsNoTracking().Where(r => loadIds.Contains(r.SourceLoadId))
            .ToDictionaryAsync(r => r.SourceLoadId, cancellationToken);
        var loadsByPour = loads.ToLookup(l => l.PourId);

        StepDelayDto? Step(DateTime? plannedLocal, DateTime? actualUtc, bool approved)
        {
            if (plannedLocal is null) return null;

            var planned = DateTime.SpecifyKind(plannedLocal.Value - offset, DateTimeKind.Utc);
            if (!approved || (actualUtc is null && nowUtc <= planned))
            {
                return new StepDelayDto(planned, actualUtc, null, false);
            }

            var minutes = (int)Math.Floor(((actualUtc ?? nowUtc) - planned).TotalMinutes);
            return new StepDelayDto(planned, actualUtc, minutes, minutes > settings.LateThresholdMinutes);
        }

        var livePlans = plans.Select(plan =>
        {
            var approved = plan.Status == ShiftPlanStatus.Approved;

            var livePours = pours.Where(p => p.ShiftPlanId == plan.Id).OrderBy(p => p.PlannedStart).Select(p =>
            {
                var mine = loadsByPour[p.Id].ToList();
                var live = mine.Where(l => l.CancelledAtUtc is null && l.RejectedAtUtc is null).ToList();
                var produced = live.Sum(l => l.QuantityCft);
                var delivered = mine.Sum(l => DeliveredQuantity.Contribution(l, records.GetValueOrDefault(l.Id)));

                var pumpDispatch = p.PumpId is null ? null : Step(p.PumpLeaveAt, p.PumpDispatchedAtUtc, approved);
                var pumpReady = p.PumpId is null ? null : Step(p.PlannedStart, p.PumpAssembledAtUtc, approved);
                var firstTruck = Step(p.FirstTruckLeaveAt, live.Min(l => l.DispatchedAtUtc), approved);
                var pourStart = Step(p.PlannedStart, live.Min(l => l.PouringStartedAtUtc), approved);

                var late = pumpDispatch is { Late: true } || pumpReady is { Late: true }
                    || firstTruck is { Late: true } || pourStart is { Late: true };

                return new LivePourDto(
                    p.Id, p.OrderId, p.PumpId, p.PlannedCft, p.PlannedStart, produced, p.PlannedCft - produced, delivered,
                    Count(mine), p.PumpArrivedAtUtc, pumpDispatch, pumpReady, firstTruck, pourStart, late);
            }).ToList();

            var planLoads = livePours.Count == 0 ? [] : pours.Where(p => p.ShiftPlanId == plan.Id)
                .SelectMany(p => loadsByPour[p.Id]).ToList();
            var planned = livePours.Sum(p => p.PlannedCft);
            var produced = livePours.Sum(p => p.ProducedCft);

            return new LivePlanDto(
                plan.Id, plan.PlantId, plan.PlantName, plan.ShiftId, plan.ShiftName, plan.Status,
                planned, produced, planned - produced, livePours.Sum(p => p.DeliveredCft),
                Count(planLoads), livePours.Count(p => p.Late), livePours);
        })
        .OrderBy(p => p.PlantName).ThenBy(p => p.Pours.Count == 0 ? DateTime.MaxValue : p.Pours.Min(x => x.PlannedStart))
        .ToList();

        return new LiveDashboardDto(
            day, settings.LateThresholdMinutes, nowUtc, livePlans.Sum(p => p.PlannedCft), livePlans.Sum(p => p.ProducedCft),
            livePlans.Sum(p => p.DeliveredCft), Count(loads), livePlans.Sum(p => p.LatePours), livePlans);
    }

    // Approved shift plans in the range, by plant, by day and by product.
    [HttpGet("api/dashboards/production")]
    [HasPermission(Permissions.DashboardsView)]
    public async Task<ActionResult<ProductionDashboardDto>> Production(
        DateOnly? from, DateOnly? to, Guid? plantId, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var last = to ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(settings.UtcOffsetMinutes));
        var first = from ?? last.AddDays(-6);
        if (RangeProblem(first, last) is { } problem) return problem;

        var scope = db.Pours.AsNoTracking().Where(p =>
            p.ShiftPlan.Status == ShiftPlanStatus.Approved && p.ShiftPlan.Date >= first && p.ShiftPlan.Date <= last
            && (plantId == null || p.ShiftPlan.PlantId == plantId));

        var pours = await scope.Select(p => new
        {
            p.Id,
            p.ShiftPlan.PlantId,
            PlantName = p.ShiftPlan.Plant.Name,
            p.ShiftPlan.Date,
            ProductId = p.OrderLine.ProductId,
            p.PlannedCft
        }).ToListAsync(cancellationToken);

        var scopeIds = scope.Select(p => p.Id);
        var loads = await db.TruckLoads.AsNoTracking().Where(l => scopeIds.Contains(l.PourId)).ToListAsync(cancellationToken);
        var loadIds = loads.Select(l => l.Id).ToList();
        var records = await db.ReturnedConcretes.AsNoTracking().Where(r => loadIds.Contains(r.SourceLoadId))
            .ToDictionaryAsync(r => r.SourceLoadId, cancellationToken);
        var loadsByPour = loads.ToLookup(l => l.PourId);

        var productIds = pours.Select(p => p.ProductId).Distinct().ToList();
        var productNames = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        ProductionFiguresDto Figures(IEnumerable<Guid> pourIds, decimal planned)
        {
            var mine = pourIds.SelectMany(id => loadsByPour[id]).ToList();
            var counted = mine.Where(l => l.CancelledAtUtc is null).ToList();
            var mineRecords = counted.Select(l => records.GetValueOrDefault(l.Id)).Where(r => r is not null).Select(r => r!).ToList();

            return new ProductionFiguresDto(
                planned,
                counted.Where(l => l.RejectedAtUtc is null).Sum(l => l.QuantityCft),
                mine.Sum(l => DeliveredQuantity.Contribution(l, records.GetValueOrDefault(l.Id))),
                mineRecords.Sum(r => r.QuantityCft),
                mineRecords.Sum(r => r.DumpedCft),
                pourIds.Count(),
                counted.Count,
                counted.Count(l => l.RejectedAtUtc is not null));
        }

        ProductionFiguresDto Group(IEnumerable<Guid> ids) =>
            Figures(ids, pours.Where(p => ids.Contains(p.Id)).Sum(p => p.PlannedCft));

        return new ProductionDashboardDto(
            first, last,
            Group(pours.Select(p => p.Id).ToList()),
            pours.GroupBy(p => new { p.PlantId, p.PlantName })
                .Select(g => new PlantFiguresDto(g.Key.PlantId, g.Key.PlantName, Group(g.Select(p => p.Id).ToList())))
                .OrderBy(p => p.PlantName).ToList(),
            pours.GroupBy(p => p.Date)
                .Select(g => new DayFiguresDto(g.Key, Group(g.Select(p => p.Id).ToList())))
                .OrderBy(d => d.Date).ToList(),
            pours.GroupBy(p => p.ProductId)
                .Select(g => new ProductFiguresDto(
                    g.Key, productNames.GetValueOrDefault(g.Key) ?? "", Group(g.Select(p => p.Id).ToList())))
                .OrderBy(p => p.ProductName).ToList());
    }

    // Orders by status (requested date in the range) and what was invoiced (closed in the range).
    [HttpGet("api/dashboards/orders")]
    [HasPermission(Permissions.DashboardsView)]
    public async Task<ActionResult<OrdersDashboardDto>> Orders(
        DateOnly? from, DateOnly? to, Guid? plantId, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var offset = TimeSpan.FromMinutes(settings.UtcOffsetMinutes);
        var last = to ?? DateOnly.FromDateTime(DateTime.UtcNow + offset);
        var first = from ?? last.AddDays(-6);
        if (RangeProblem(first, last) is { } problem) return problem;

        // Closing times are UTC, so the local days are turned into UTC bounds.
        var fromUtc = first.ToDateTime(TimeOnly.MinValue) - offset;
        var toUtc = last.AddDays(1).ToDateTime(TimeOnly.MinValue) - offset;

        var statuses = await db.Orders.AsNoTracking()
            .Where(o => o.RequestedDate >= first && o.RequestedDate <= last && (plantId == null || o.PlantId == plantId))
            .GroupBy(o => new { o.PlantId, o.Status })
            .Select(g => new
            {
                g.Key.PlantId,
                g.Key.Status,
                Orders = g.Count(),
                Value = g.Sum(o => (decimal?)o.ConfirmedTotal) ?? 0m
            }).ToListAsync(cancellationToken);

        var invoiced = await db.Orders.AsNoTracking()
            .Where(o => o.ClosedAtUtc != null && o.ClosedAtUtc >= fromUtc && o.ClosedAtUtc < toUtc
                && (plantId == null || o.PlantId == plantId))
            .GroupBy(o => o.PlantId)
            .Select(g => new
            {
                PlantId = g.Key,
                Orders = g.Count(),
                Subtotal = g.Sum(o => (decimal?)o.InvoiceSubtotal) ?? 0m,
                Vat = g.Sum(o => (decimal?)o.InvoiceVatAmount) ?? 0m,
                Total = g.Sum(o => (decimal?)o.InvoiceTotal) ?? 0m
            }).ToListAsync(cancellationToken);

        var plantIds = statuses.Select(s => s.PlantId).Union(invoiced.Select(i => i.PlantId)).Distinct().ToList();
        var plantNames = await db.Plants.AsNoTracking().Where(p => plantIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        static List<OrderStatusCountDto> ByStatus<T>(IEnumerable<T> rows, Func<T, OrderStatus> status, Func<T, int> count, Func<T, decimal> value) =>
            rows.GroupBy(status)
                .Select(g => new OrderStatusCountDto(g.Key, g.Sum(count), g.Sum(value)))
                .OrderBy(s => s.Status).ToList();

        return new OrdersDashboardDto(
            first, last,
            ByStatus(statuses, s => s.Status, s => s.Orders, s => s.Value),
            new InvoicedDto(invoiced.Sum(i => i.Orders), invoiced.Sum(i => i.Subtotal), invoiced.Sum(i => i.Vat), invoiced.Sum(i => i.Total)),
            plantIds.Select(id => new PlantOrdersDto(
                id, plantNames.GetValueOrDefault(id) ?? "",
                ByStatus(statuses.Where(s => s.PlantId == id), s => s.Status, s => s.Orders, s => s.Value),
                invoiced.Where(i => i.PlantId == id).Select(i => new InvoicedDto(i.Orders, i.Subtotal, i.Vat, i.Total))
                    .FirstOrDefault() ?? new InvoicedDto(0, 0m, 0m, 0m)))
                .OrderBy(p => p.PlantName).ToList());
    }

    private async Task<(int LateThresholdMinutes, int UtcOffsetMinutes)> SettingsAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var settings = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.LateThresholdMinutes, t.UtcOffsetMinutes }).FirstAsync(cancellationToken);
        return (settings.LateThresholdMinutes, settings.UtcOffsetMinutes);
    }

    private ObjectResult? RangeProblem(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return Problem(title: "The end date cannot be before the start date.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Problem(title: $"A range can be at most {MaxDays} days.", statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    // Loads that are still part of the pour, by where they are now.
    private static LoadCountsDto Count(IEnumerable<TruckLoad> loads)
    {
        var live = loads.Where(l => l.CancelledAtUtc is null && l.RejectedAtUtc is null).ToList();
        return new LoadCountsDto(
            live.Count(l => l.DispatchedAtUtc is null),
            live.Count(l => l.DispatchedAtUtc is not null && l.ArrivedAtUtc is null),
            live.Count(l => l.ArrivedAtUtc is not null && l.PouringStartedAtUtc is null),
            live.Count(l => l.PouringStartedAtUtc is not null && l.PouringEndedAtUtc is null),
            live.Count(l => l.PouringEndedAtUtc is not null && l.ReturnedAtUtc is null),
            live.Count(l => l.ReturnedAtUtc is not null));
    }
}