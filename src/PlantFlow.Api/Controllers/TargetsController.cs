using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Targets;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// One company, plant or rep in a quarter: its target (if set) and what it has billed so far.
// BilledCft comes from closed orders and is final. ProvisionalCft is delivered volume on orders still open,
// only shown for the quarter running today.
public record TargetRowDto(
    TargetLevel Level, Guid? PlantId, Guid? SalesRepId, string Name, string? Code,
    Guid? TargetId, decimal? TargetCft, decimal BilledCft, decimal ProvisionalCft);

// EndDate is the quarter's last day. Ended quarters can't change. A sales rep only gets their own row in Reps.
public record TargetQuarterDto(
    int Year, int Quarter, string Label, DateOnly StartDate, DateOnly EndDate, bool Current, bool Ended,
    int YearStartMonth, TargetRowDto? Company, List<TargetRowDto> Plants, List<TargetRowDto> Reps, List<string> Warnings);

public class TargetRequest
{
    [Range(2000, 2100)]
    public int Year { get; init; }

    [Range(1, 4)]
    public int Quarter { get; init; }

    [Required]
    public TargetLevel? Level { get; init; }

    public Guid? PlantId { get; init; }
    public Guid? SalesRepId { get; init; }

    [Range(0.01, 1_000_000_000.0)]
    public decimal VolumeCft { get; init; }
}

[ApiController]
public class TargetsController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, CustomerOwnership ownership) : ControllerBase
{
    public const string EndedMessage = "This quarter has ended, so its targets can't change.";

    private static readonly OrderStatus[] OpenStatuses =
        [OrderStatus.Confirmed, OrderStatus.InProgress, OrderStatus.OnHold, OrderStatus.Completed];

    // A quarter's targets and achievement. Without year and quarter, the quarter running today.
    [HttpGet("api/targets")]
    [HasPermission(Permissions.TargetsView)]
    public async Task<ActionResult<TargetQuarterDto>> Get(
        [FromQuery] int? year, [FromQuery] int? quarter, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var (y, q) = year is { } yy && quarter is { } qq ? (yy, qq) : TargetQuarters.Of(settings.Today, settings.StartMonth);
        if (y is < 2000 or > 2100 || q is < 1 or > 4)
            return Problem(title: "Choose a quarter from 1 to 4 of a year from 2000 to 2100.", statusCode: StatusCodes.Status400BadRequest);

        return await QuarterAsync(y, q, settings, cancellationToken);
    }

    // Sets the target of the company, a plant or a rep for one quarter. No approval; every change is audited.
    [HttpPut("api/targets")]
    [HasPermission(Permissions.TargetsManage)]
    public async Task<ActionResult<TargetQuarterDto>> Set(TargetRequest request, CancellationToken cancellationToken)
    {
        var level = request.Level!.Value;
        var refusal = level switch
        {
            TargetLevel.Company when request.PlantId is not null || request.SalesRepId is not null =>
                "A company target is for the whole company, without a plant or sales rep.",
            TargetLevel.Plant when request.PlantId is null || request.SalesRepId is not null =>
                "Choose the plant for a plant target.",
            TargetLevel.Rep when request.SalesRepId is null || request.PlantId is not null =>
                "Choose the sales rep for a rep target.",
            _ => null
        };
        if (refusal is null && request.VolumeCft != Math.Round(request.VolumeCft, 2))
            refusal = "A target can have at most 2 decimal places.";
        if (refusal is not null) return Problem(title: refusal, statusCode: StatusCodes.Status400BadRequest);

        if (request.PlantId is { } plantId && !await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
            return Problem(title: "That plant was not found.", statusCode: StatusCodes.Status400BadRequest);
        if (request.SalesRepId is { } repId && !await db.SalesReps.AnyAsync(r => r.Id == repId, cancellationToken))
            return Problem(title: "That sales rep was not found.", statusCode: StatusCodes.Status400BadRequest);

        var settings = await SettingsAsync(cancellationToken);
        if (Ended(request.Year, request.Quarter, settings))
            return Problem(title: EndedMessage, statusCode: StatusCodes.Status409Conflict);

        var target = await db.VolumeTargets.FirstOrDefaultAsync(t =>
            t.Year == request.Year && t.Quarter == request.Quarter && t.Level == level
            && t.PlantId == request.PlantId && t.SalesRepId == request.SalesRepId, cancellationToken);
        var before = target?.VolumeCft;
        if (target is null)
        {
            target = new VolumeTarget
            {
                Year = request.Year, Quarter = request.Quarter, Level = level,
                PlantId = request.PlantId, SalesRepId = request.SalesRepId
            };
            db.VolumeTargets.Add(target);
        }
        target.VolumeCft = request.VolumeCft;

        audit.Record(currentUser.TenantId!.Value, "target.set", "VolumeTarget", target.Id.ToString(), new
        {
            target.Year, target.Quarter, Level = level.ToString(), target.PlantId, target.SalesRepId,
            Before = before, After = target.VolumeCft
        });
        await db.SaveChangesAsync(cancellationToken);

        return await QuarterAsync(request.Year, request.Quarter, settings, cancellationToken);
    }

    [HttpDelete("api/targets/{id:guid}")]
    [HasPermission(Permissions.TargetsManage)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var target = await db.VolumeTargets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null) return NotFound();

        var settings = await SettingsAsync(cancellationToken);
        if (Ended(target.Year, target.Quarter, settings))
            return Problem(title: EndedMessage, statusCode: StatusCodes.Status409Conflict);

        db.VolumeTargets.Remove(target);
        audit.Record(currentUser.TenantId!.Value, "target.removed", "VolumeTarget", target.Id.ToString(), new
        {
            target.Year, target.Quarter, Level = target.Level.ToString(), target.PlantId, target.SalesRepId,
            Before = target.VolumeCft
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private sealed record Settings(int StartMonth, int UtcOffsetMinutes, DateOnly Today);

    private async Task<Settings> SettingsAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        var t = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.YearStartMonth, t.UtcOffsetMinutes }).FirstAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(t.UtcOffsetMinutes));
        return new Settings(t.YearStartMonth, t.UtcOffsetMinutes, today);
    }

    private static bool Ended(int year, int quarter, Settings settings) =>
        TargetQuarters.End(year, quarter, settings.StartMonth) <= settings.Today;

    private async Task<TargetQuarterDto> QuarterAsync(int year, int quarter, Settings settings, CancellationToken cancellationToken)
    {
        var start = TargetQuarters.Start(year, quarter, settings.StartMonth);
        var end = TargetQuarters.End(year, quarter, settings.StartMonth);
        var current = start <= settings.Today && settings.Today < end;
        var ended = end <= settings.Today;

        // Close dates are UTC; the quarter is in the plants' local time.
        var offset = TimeSpan.FromMinutes(settings.UtcOffsetMinutes);
        var startUtc = start.ToDateTime(TimeOnly.MinValue) - offset;
        var endUtc = end.ToDateTime(TimeOnly.MinValue) - offset;

        var closed = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.ClosedAtUtc >= startUtc && o.ClosedAtUtc < endUtc)
            .Select(o => new
            {
                o.PlantId,
                o.SalesRepId,
                Lines = o.Lines.Select(l => new { l.QuantityCft, l.DeliveredCft, l.BilledCft }).ToList()
            })
            .ToListAsync(cancellationToken);
        var billed = closed.Select(o => new Figure(o.PlantId, o.SalesRepId,
            o.Lines.Sum(l => CustomerLedger.EffectiveBilled(l.QuantityCft, l.DeliveredCft, l.BilledCft)))).ToList();

        // Orders still open count as provisional in the quarter running today, as if they closed now.
        var provisional = current
            ? await db.Orders.AsNoTracking()
                .Where(o => OpenStatuses.Contains(o.Status) && o.ClosedAtUtc == null)
                .Select(o => new { o.PlantId, o.SalesRepId, Delivered = o.Lines.Sum(l => l.DeliveredCft) })
                .Where(o => o.Delivered > 0)
                .Select(o => new Figure(o.PlantId, o.SalesRepId, o.Delivered))
                .ToListAsync(cancellationToken)
            : [];

        var targets = await db.VolumeTargets.AsNoTracking()
            .Where(t => t.Year == year && t.Quarter == quarter).ToListAsync(cancellationToken);

        var label = TargetQuarters.Label(year, quarter, settings.StartMonth);
        var lastDay = end.AddDays(-1);

        // A sales rep sees only their own target.
        if (CustomerOwnership.IsRep(User))
        {
            var myRepId = await ownership.MyRepIdAsync(cancellationToken);
            var mine = new List<TargetRowDto>();
            if (myRepId is { } id)
            {
                var name = await db.SalesReps.Where(r => r.Id == id).Select(r => r.Name).FirstAsync(cancellationToken);
                mine.Add(RepRow(id, name, targets, billed, provisional));
            }
            return new TargetQuarterDto(year, quarter, label, start, lastDay, current, ended, settings.StartMonth,
                null, [], mine, []);
        }

        var companyTarget = targets.FirstOrDefault(t => t.Level == TargetLevel.Company);
        var company = new TargetRowDto(TargetLevel.Company, null, null, "Company", null,
            companyTarget?.Id, companyTarget?.VolumeCft, billed.Sum(f => f.Cft), provisional.Sum(f => f.Cft));

        // Active plants and reps, plus inactive ones that still have a target or volume in this quarter.
        var plantIds = targets.Where(t => t.PlantId != null).Select(t => t.PlantId!.Value)
            .Concat(billed.Concat(provisional).Select(f => f.PlantId)).Distinct().ToList();
        var plants = await db.Plants.AsNoTracking()
            .Where(p => p.Status == ActiveStatus.Active || plantIds.Contains(p.Id))
            .OrderBy(p => p.Code).Select(p => new { p.Id, p.Code, p.Name }).ToListAsync(cancellationToken);
        var plantRows = plants.Select(p =>
        {
            var target = targets.FirstOrDefault(t => t.Level == TargetLevel.Plant && t.PlantId == p.Id);
            return new TargetRowDto(TargetLevel.Plant, p.Id, null, p.Name, p.Code, target?.Id, target?.VolumeCft,
                billed.Where(f => f.PlantId == p.Id).Sum(f => f.Cft),
                provisional.Where(f => f.PlantId == p.Id).Sum(f => f.Cft));
        }).ToList();

        var repIds = targets.Where(t => t.SalesRepId != null).Select(t => t.SalesRepId!.Value)
            .Concat(billed.Concat(provisional).Where(f => f.SalesRepId != null).Select(f => f.SalesRepId!.Value))
            .Distinct().ToList();
        var reps = await db.SalesReps.AsNoTracking()
            .Where(r => r.Status == ActiveStatus.Active || repIds.Contains(r.Id))
            .OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken);
        var repRows = reps.Select(r => RepRow(r.Id, r.Name, targets, billed, provisional)).ToList();

        // Children may add up to more than the company target (stretch targets), but say so.
        var warnings = new List<string>();
        if (companyTarget is not null)
        {
            var plantSum = plantRows.Sum(r => r.TargetCft ?? 0);
            if (plantSum > companyTarget.VolumeCft)
                warnings.Add($"The plant targets add up to {plantSum:#,0.##} CFT, more than the company target of {companyTarget.VolumeCft:#,0.##} CFT.");
            var repSum = repRows.Sum(r => r.TargetCft ?? 0);
            if (repSum > companyTarget.VolumeCft)
                warnings.Add($"The sales rep targets add up to {repSum:#,0.##} CFT, more than the company target of {companyTarget.VolumeCft:#,0.##} CFT.");
        }

        return new TargetQuarterDto(year, quarter, label, start, lastDay, current, ended, settings.StartMonth,
            company, plantRows, repRows, warnings);
    }

    private sealed record Figure(Guid PlantId, Guid? SalesRepId, decimal Cft);

    private static TargetRowDto RepRow(
        Guid repId, string name, List<VolumeTarget> targets, List<Figure> billed, List<Figure> provisional)
    {
        var target = targets.FirstOrDefault(t => t.Level == TargetLevel.Rep && t.SalesRepId == repId);
        return new TargetRowDto(TargetLevel.Rep, null, repId, name, null, target?.Id, target?.VolumeCft,
            billed.Where(f => f.SalesRepId == repId).Sum(f => f.Cft),
            provisional.Where(f => f.SalesRepId == repId).Sum(f => f.Cft));
    }
}
