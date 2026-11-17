using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record ReturnedConcreteDto(
    Guid Id, Guid SourceLoadId, Guid PlantId, Guid ShiftPlanId, Guid ProductId, ReturnKind Kind, ReturnFault Fault,
    string Reason, decimal QuantityCft, decimal RemainingCft, decimal DumpedCft, ReturnStatus Status, string? Note,
    DateTime CreatedAtUtc, DateTime? DecidedAtUtc, string? ProductCode = null);

public class RejectLoadRequest
{
    // Needed for a reject at the site. A reject at the plant is always the company's fault.
    public ReturnFault? Fault { get; init; }

    [Required, StringLength(300, MinimumLength = 3)]
    public string? Reason { get; init; }
}

public class PartReturnRequest
{
    // What came back, at most 2 decimals and less than the load.
    [Required, Range(0.01, 100000.0)]
    public decimal? QuantityCft { get; init; }

    [Required]
    public ReturnFault? Fault { get; init; }

    [Required, StringLength(300, MinimumLength = 3)]
    public string? Reason { get; init; }
}

public class DecideRequest
{
    [Required]
    public ReturnDecision? Decision { get; init; }

    // Needed when dumping.
    [StringLength(300)]
    public string? Note { get; init; }
}

[ApiController]
public class ReturnsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    // The latest 200, newest first. The operator reads this to find concrete he can use.
    [HttpGet("api/returns")]
    [HasPermission(Permissions.BatchesManage, Permissions.ReturnsQc)]
    public async Task<ActionResult<List<ReturnedConcreteDto>>> List(
        Guid? plantId, Guid? shiftPlanId, ReturnStatus? status, CancellationToken cancellationToken)
    {
        var query = db.ReturnedConcretes.AsNoTracking().AsQueryable();
        if (plantId is { } plant) query = query.Where(r => r.PlantId == plant);
        if (shiftPlanId is { } plan) query = query.Where(r => r.ShiftPlanId == plan);
        if (status is { } s) query = query.Where(r => r.Status == s);

        var rows = await query.OrderByDescending(r => r.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
        var codes = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);
        return rows.Select(r => ToDto(r) with { ProductCode = codes.GetValueOrDefault(r.ProductId) }).ToList();
    }

    // A whole load refused before pouring started: by QC at the plant, or at the site.
    [HttpPost("api/loads/{id:guid}/reject")]
    [HasPermission(Permissions.ReturnsManage)]
    public async Task<ActionResult<ReturnedConcreteDto>> Reject(Guid id, RejectLoadRequest request, CancellationToken cancellationToken)
    {
        var load = await db.TruckLoads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (load is null) return NotFound();
        if (load.CancelledAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load was cancelled.");
        if (load.RejectedAtUtc is not null) return Fail(StatusCodes.Status409Conflict, "This load was already rejected.");
        if (load.PouringStartedAtUtc is not null)
        {
            return Fail(StatusCodes.Status409Conflict, "Pouring has started. Record a part return instead.");
        }

        var atPlant = load.DispatchedAtUtc is null;
        if (!atPlant && load.ArrivedAtUtc is null)
        {
            return Fail(StatusCodes.Status409Conflict, "Record the truck's arrival first. A load that never arrived is cancelled instead.");
        }

        ReturnFault fault;
        if (atPlant)
        {
            if (request.Fault == ReturnFault.Customer)
            {
                return Fail(StatusCodes.Status400BadRequest, "A reject at the plant is always the company's fault.");
            }

            fault = ReturnFault.Company;
        }
        else
        {
            if (request.Fault is not { } chosen)
            {
                return Fail(StatusCodes.Status400BadRequest, "Say whether the company or the customer is at fault.");
            }

            fault = chosen;
        }

        var now = DateTime.UtcNow;
        var reason = request.Reason!.Trim();
        load.RejectedAtUtc = now;
        load.RejectReason = reason;

        // A load that never left the plant has not been anywhere, so its truck is free.
        if (atPlant) load.ReturnedAtUtc = now;

        var record = await NewRecordAsync(
            load, atPlant ? ReturnKind.RejectedAtPlant : ReturnKind.RejectedAtSite, fault, reason, load.QuantityCft, cancellationToken);
        db.ReturnedConcretes.Add(record);

        audit.Record(load.TenantId, "load.rejected", "TruckLoad", load.Id.ToString(),
            new { load.PourId, Kind = record.Kind.ToString(), Fault = fault.ToString(), reason, load.QuantityCft });
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/returns/{record.Id}", ToDto(record));
    }

    // Leftover concrete after pouring, brought back to the plant. It has to be recorded within the company's window.
    [HttpPost("api/loads/{id:guid}/part-return")]
    [HasPermission(Permissions.ReturnsManage)]
    public Task<ActionResult<ReturnedConcreteDto>> PartReturn(Guid id, PartReturnRequest request, CancellationToken cancellationToken) =>
        PartReturnAsync(id, request, late: false, cancellationToken);

    // After the window only a plant manager can record it. It is kept in the audit trail as late.
    [HttpPost("api/loads/{id:guid}/part-return/late")]
    [HasPermission(Permissions.DeliveriesPlanOverride)]
    public Task<ActionResult<ReturnedConcreteDto>> PartReturnLate(Guid id, PartReturnRequest request, CancellationToken cancellationToken) =>
        PartReturnAsync(id, request, late: true, cancellationToken);

    private async Task<ActionResult<ReturnedConcreteDto>> PartReturnAsync(
        Guid id, PartReturnRequest request, bool late, CancellationToken cancellationToken)
    {
        var load = await db.TruckLoads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (load is null) return NotFound();
        if (load.CancelledAtUtc is not null || load.RejectedAtUtc is not null)
        {
            return Fail(StatusCodes.Status409Conflict, "This load was cancelled or rejected.");
        }

        if (load.PouringEndedAtUtc is null)
        {
            return Fail(StatusCodes.Status409Conflict, "Pouring has to end before a part load can be returned.");
        }

        var windowMinutes = await db.Tenants.AsNoTracking().Where(t => t.Id == currentUser.TenantId)
            .Select(t => t.ReturnWindowMinutes).FirstAsync(cancellationToken);
        var closesAt = load.PouringEndedAtUtc!.Value.AddMinutes(windowMinutes);
        var pastWindow = DateTime.UtcNow > closesAt;
        if (pastWindow && !late)
        {
            return Fail(StatusCodes.Status409Conflict,
                $"A return has to be recorded within {windowMinutes} minutes of pouring ending. A plant manager can still record it late.");
        }

        if (pastWindow)
        {
            audit.Record(load.TenantId, "load.part_return_late", "TruckLoad", load.Id.ToString(), new { windowMinutes, closesAt });
        }

        if (await db.ReturnedConcretes.AnyAsync(r => r.SourceLoadId == id, cancellationToken))
        {
            return Fail(StatusCodes.Status409Conflict, "A return was already recorded for this load.");
        }

        var quantity = request.QuantityCft!.Value;
        if (quantity != Math.Round(quantity, 2))
        {
            return Fail(StatusCodes.Status400BadRequest, "Quantity can have at most 2 decimals.");
        }

        if (quantity >= load.QuantityCft)
        {
            return Fail(StatusCodes.Status400BadRequest, "A part return must be less than the whole load.");
        }

        var reason = request.Reason!.Trim();
        load.ReturnedCft = quantity;

        var record = await NewRecordAsync(load, ReturnKind.PartLoad, request.Fault!.Value, reason, quantity, cancellationToken);
        db.ReturnedConcretes.Add(record);

        audit.Record(load.TenantId, "load.part_returned", "TruckLoad", load.Id.ToString(),
            new { load.PourId, quantity, Fault = record.Fault.ToString(), reason });
        await db.SaveChangesAsync(cancellationToken);

        await RecalculateForLoadAsync(load.PourId, cancellationToken);
        return Created($"api/returns/{record.Id}", ToDto(record));
    }

    // QC's decision after inspecting, treating or giving up on the concrete. Recording a return is not enough.
    [HttpPost("api/returns/{id:guid}/decide")]
    [HasPermission(Permissions.ReturnsQc)]
    public async Task<ActionResult<ReturnedConcreteDto>> Decide(Guid id, DecideRequest request, CancellationToken cancellationToken)
    {
        var record = await db.ReturnedConcretes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (record is null) return NotFound();

        var decision = request.Decision!.Value;
        var allowed = (record.Status, decision) switch
        {
            (ReturnStatus.AwaitingInspection,
                ReturnDecision.Reusable or ReturnDecision.NeedsTreatment or ReturnDecision.Dumped) => true,
            (ReturnStatus.NeedsTreatment, ReturnDecision.Treated or ReturnDecision.Dumped) => true,
            (ReturnStatus.Reusable or ReturnStatus.Treated, ReturnDecision.Dumped) => record.RemainingCft > 0,
            _ => false
        };
        if (!allowed)
        {
            return Fail(StatusCodes.Status409Conflict, $"Concrete that is {record.Status} cannot become {decision}.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (decision == ReturnDecision.Dumped && note is null)
        {
            return Fail(StatusCodes.Status400BadRequest, "Give a reason for dumping.");
        }

        record.Status = decision switch
        {
            ReturnDecision.Reusable => ReturnStatus.Reusable,
            ReturnDecision.NeedsTreatment => ReturnStatus.NeedsTreatment,
            ReturnDecision.Treated => ReturnStatus.Treated,
            _ => ReturnStatus.Dumped
        };

        if (decision == ReturnDecision.Dumped)
        {
            record.DumpedCft = record.RemainingCft;
            record.RemainingCft = 0m;
        }

        record.Note = note ?? record.Note;
        record.DecidedBy = currentUser.UserId;
        record.DecidedAtUtc = DateTime.UtcNow;

        audit.Record(record.TenantId, "return.decided", "ReturnedConcrete", record.Id.ToString(),
            new { Decision = decision.ToString(), record.DumpedCft, record.Note });
        await db.SaveChangesAsync(cancellationToken);

        // A dump can put a customer-fault part back on the bill.
        var pourId = await db.TruckLoads.Where(l => l.Id == record.SourceLoadId).Select(l => l.PourId).FirstAsync(cancellationToken);
        await RecalculateForLoadAsync(pourId, cancellationToken);

        return ToDto(record);
    }

    private async Task<ReturnedConcrete> NewRecordAsync(
        TruckLoad load, ReturnKind kind, ReturnFault fault, string reason, decimal quantity, CancellationToken cancellationToken)
    {
        var info = await db.Pours.AsNoTracking().Where(p => p.Id == load.PourId)
            .Select(p => new { p.ShiftPlanId, p.ShiftPlan.PlantId, p.OrderLine.ProductId })
            .FirstAsync(cancellationToken);

        return new ReturnedConcrete
        {
            TenantId = load.TenantId,
            PlantId = info.PlantId,
            ShiftPlanId = info.ShiftPlanId,
            ProductId = info.ProductId,
            SourceLoadId = load.Id,
            Kind = kind,
            Fault = fault,
            Reason = reason,
            QuantityCft = quantity,
            RemainingCft = quantity,
            CreatedBy = currentUser.UserId
        };
    }

    private async Task RecalculateForLoadAsync(Guid pourId, CancellationToken cancellationToken)
    {
        var lineId = await db.Pours.Where(p => p.Id == pourId).Select(p => p.OrderLineId).FirstAsync(cancellationToken);
        await DeliveredQuantity.RecalculateAsync(db, audit, lineId, cancellationToken);
    }

    private ObjectResult Fail(int status, string title) => Problem(title: title, statusCode: status);

    private static ReturnedConcreteDto ToDto(ReturnedConcrete r) => new(
        r.Id, r.SourceLoadId, r.PlantId, r.ShiftPlanId, r.ProductId, r.Kind, r.Fault, r.Reason, r.QuantityCft,
        r.RemainingCft, r.DumpedCft, r.Status, r.Note, r.CreatedAtUtc, r.DecidedAtUtc);
}