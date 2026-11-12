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

public record ServiceChargeDto(
    Guid Id, string Code, string Name, ServiceChargeKind Kind, ChargeBasis Basis,
    decimal DefaultAmount, ActiveStatus Status);

public class ServiceChargeRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Nullable + Required, so a missing value is rejected instead of silently becoming the first enum value.
    [Required]
    public ServiceChargeKind? Kind { get; init; }

    [Required]
    public ChargeBasis? Basis { get; init; }

    [Range(0.0, 1_000_000_000.0)]
    public decimal DefaultAmount { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/service-charges")]
public class ServiceChargesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<ServiceCharge, ServiceChargeDto>> ToDto = s =>
        new ServiceChargeDto(s.Id, s.Code, s.Name, s.Kind, s.Basis, s.DefaultAmount, s.Status);

    // ?search= matches code or name.  ?kind=  ?basis=  ?status=  ?sort=code|name|amount, "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<ServiceChargeDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ServiceChargeKind? kind, [FromQuery] ChargeBasis? basis,
        [FromQuery] ActiveStatus? status, CancellationToken cancellationToken)
    {
        var query = db.ServiceCharges.AsNoTracking();

        if (kind is not null) query = query.Where(s => s.Kind == kind);
        if (basis is not null) query = query.Where(s => s.Basis == basis);
        if (status is not null) query = query.Where(s => s.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(s => s.Name),
            "-name" => query.OrderByDescending(s => s.Name),
            "amount" => query.OrderBy(s => s.DefaultAmount).ThenBy(s => s.Code),
            "-amount" => query.OrderByDescending(s => s.DefaultAmount).ThenBy(s => s.Code),
            "-code" => query.OrderByDescending(s => s.Code),
            _ => query.OrderBy(s => s.Code)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<ServiceChargeDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var charge = await db.ServiceCharges.AsNoTracking().Where(s => s.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (charge is null) return NotFound();
        return charge;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<ServiceChargeDto>> Create(ServiceChargeRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var problem = await CheckAsync(request, code, null, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var charge = new ServiceCharge
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Kind = request.Kind!.Value,
            Basis = request.Basis!.Value,
            DefaultAmount = request.DefaultAmount,
            Status = request.Status
        };

        db.ServiceCharges.Add(charge);
        audit.Record(tenantId, "servicecharge.created", "ServiceCharge", charge.Id.ToString(),
            new { charge.Code, Kind = charge.Kind.ToString(), charge.DefaultAmount });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.ServiceCharges.AsNoTracking().Where(s => s.Id == charge.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = charge.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, ServiceChargeRequest request, CancellationToken cancellationToken)
    {
        var charge = await db.ServiceCharges.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (charge is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        var problem = await CheckAsync(request, code, id, cancellationToken);
        if (problem is not null) return problem;

        charge.Code = code;
        charge.Name = request.Name.Trim();
        charge.Kind = request.Kind!.Value;
        charge.Basis = request.Basis!.Value;
        charge.DefaultAmount = request.DefaultAmount;
        charge.Status = request.Status;

        audit.Record(charge.TenantId, "servicecharge.updated", "ServiceCharge", charge.Id.ToString(),
            new { charge.Code, charge.DefaultAmount, Status = charge.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckAsync(
        ServiceChargeRequest request, string code, Guid? selfId, CancellationToken cancellationToken)
    {
        // Money has two decimals. Refuse extra digits instead of rounding them away silently.
        if (request.DefaultAmount != Math.Round(request.DefaultAmount, 2))
        {
            return Problem(title: "The amount can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.ServiceCharges.AnyAsync(s => s.Code == code && s.Id != selfId, cancellationToken))
        {
            return Problem(title: "A service charge with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var kind = request.Kind!.Value;
        if (await db.ServiceCharges.AnyAsync(s => s.Kind == kind && s.Id != selfId, cancellationToken))
        {
            return Problem(title: $"A {kind} charge already exists. Each company has one charge per kind.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
}