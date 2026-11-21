using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// Phone is null only for operators carried over from the old crew names, until someone adds it.
public record PumpOperatorDto(Guid Id, string Name, string? Phone, Guid? PumpId, string? PumpCode, ActiveStatus Status);

public class PumpOperatorRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [Required(ErrorMessage = "Enter the operator's phone number."), StringLength(30, MinimumLength = 5)]
    public string Phone { get; init; } = "";

    // The pump whose crew this operator is in. Empty for none. Setting it moves them off any other pump.
    public Guid? PumpId { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
public class PumpOperatorsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    public const string PumpOnly = "Choose a pump of this company that is not retired.";

    private static readonly Expression<Func<PumpOperator, PumpOperatorDto>> ToDto = o => new PumpOperatorDto(
        o.Id, o.Name, o.Phone, o.PumpId, o.Pump != null ? o.Pump.Code : null, o.Status);

    // ?search= matches name or phone.  ?pumpId=  ?status=
    [HttpGet("api/pump-operators")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<PumpOperatorDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? pumpId, [FromQuery] ActiveStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.PumpOperators.AsNoTracking();
        if (pumpId is not null) query = query.Where(o => o.PumpId == pumpId);
        if (status is not null) query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(o => o.Name.Contains(search) || (o.Phone != null && o.Phone.Contains(search)));
        }

        return await query.OrderBy(o => o.Name).Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("api/pump-operators/{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PumpOperatorDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var found = await db.PumpOperators.AsNoTracking().Where(o => o.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (found is null) return NotFound();
        return found;
    }

    // A pump's crew: its active operators.
    [HttpGet("api/vehicles/{id:guid}/crew")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<List<PumpOperatorDto>>> Crew(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == id, cancellationToken)) return NotFound();

        return await db.PumpOperators.AsNoTracking()
            .Where(o => o.PumpId == id && o.Status == ActiveStatus.Active)
            .OrderBy(o => o.Name).Select(ToDto).ToListAsync(cancellationToken);
    }

    [HttpPost("api/pump-operators")]
    [HasPermission(Permissions.FleetManage)]
    public async Task<ActionResult<PumpOperatorDto>> Create(PumpOperatorRequest request, CancellationToken cancellationToken)
    {
        if (request.PumpId is { } pumpId && !await IsUsablePumpAsync(pumpId, cancellationToken))
        {
            return Problem(title: PumpOnly, statusCode: StatusCodes.Status400BadRequest);
        }

        var tenantId = currentUser.TenantId!.Value;
        var created = new PumpOperator
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            PumpId = request.PumpId,
            Status = request.Status
        };

        db.PumpOperators.Add(created);
        audit.Record(tenantId, "pumpoperator.created", "PumpOperator", created.Id.ToString(),
            new { created.Name, created.Phone, created.PumpId });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.PumpOperators.AsNoTracking().Where(o => o.Id == created.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, dto);
    }

    [HttpPut("api/pump-operators/{id:guid}")]
    [HasPermission(Permissions.FleetManage)]
    public async Task<IActionResult> Update(Guid id, PumpOperatorRequest request, CancellationToken cancellationToken)
    {
        var found = await db.PumpOperators.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (found is null) return NotFound();

        if (request.PumpId != found.PumpId && request.PumpId is { } pumpId
            && !await IsUsablePumpAsync(pumpId, cancellationToken))
        {
            return Problem(title: PumpOnly, statusCode: StatusCodes.Status400BadRequest);
        }

        var previousPump = found.PumpId;
        found.Name = request.Name.Trim();
        found.Phone = request.Phone.Trim();
        found.PumpId = request.PumpId;
        found.Status = request.Status;

        audit.Record(found.TenantId, "pumpoperator.updated", "PumpOperator", found.Id.ToString(),
            new { found.Name, found.Phone, PreviousPumpId = previousPump, found.PumpId, Status = found.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<bool> IsUsablePumpAsync(Guid pumpId, CancellationToken cancellationToken) =>
        db.Vehicles.AnyAsync(v => v.Id == pumpId && v.Type == VehicleType.Pump && v.Status != VehicleStatus.Retired,
            cancellationToken);
}
