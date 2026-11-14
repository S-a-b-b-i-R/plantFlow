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

public record VehicleDto(
    Guid Id, Guid PlantId, string PlantCode, string Code, VehicleType Type, decimal? CapacityCft,
    string? PlateNumber, Guid? DefaultDriverId, string? DefaultDriverName, VehicleStatus Status, int? AssemblyMinutes);

public class VehicleRequest
{
    [Required]
    public Guid PlantId { get; init; }

    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    public VehicleType Type { get; init; } = VehicleType.TransitMixer;

    // Required for a transit mixer, optional for a pump.
    [Range(0.01, 10000)]
    public decimal? CapacityCft { get; init; }

    [StringLength(30)]
    public string? PlateNumber { get; init; }

    public Guid? DefaultDriverId { get; init; }

    public VehicleStatus Status { get; init; } = VehicleStatus.Available;

    // Minutes to assemble this pump on site. Leave it out to use the company default.
    [Range(0, 480)]
    public int? AssemblyMinutes { get; init; }
}

[ApiController]
[Route("api/vehicles")]
public class VehiclesController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Vehicle, VehicleDto>> ToDto = v => new VehicleDto(
        v.Id, v.PlantId, v.Plant.Code, v.Code, v.Type, v.CapacityCft, v.PlateNumber,
        v.DefaultDriverId, v.DefaultDriver != null ? v.DefaultDriver.Name : null, v.Status, v.AssemblyMinutes);

    // ?search= matches code or plate.  ?plantId=  ?type=  ?status=  ?sort=code|capacity, "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<VehicleDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] Guid? plantId, [FromQuery] VehicleType? type,
        [FromQuery] VehicleStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Vehicles.AsNoTracking();

        if (plantId is not null) query = query.Where(v => v.PlantId == plantId);
        if (type is not null) query = query.Where(v => v.Type == type);
        if (status is not null) query = query.Where(v => v.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(v => v.Code.Contains(search) || (v.PlateNumber != null && v.PlateNumber.Contains(search)));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "-code" => query.OrderByDescending(v => v.Code),
            "capacity" => query.OrderBy(v => v.CapacityCft).ThenBy(v => v.Code),
            "-capacity" => query.OrderByDescending(v => v.CapacityCft).ThenBy(v => v.Code),
            _ => query.OrderBy(v => v.Code)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<VehicleDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.AsNoTracking().Where(v => v.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (vehicle is null) return NotFound();
        return vehicle;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<VehicleDto>> Create(VehicleRequest request, CancellationToken cancellationToken)
    {
        var (code, plate) = Normalize(request);

        var problem = await CheckAsync(request, code, plate, null, checkPlant: true, checkDriver: true, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var vehicle = new Vehicle
        {
            TenantId = tenantId,
            PlantId = request.PlantId,
            Code = code,
            Type = request.Type,
            CapacityCft = request.CapacityCft,
            PlateNumber = plate,
            DefaultDriverId = request.DefaultDriverId,
            Status = request.Status,
            AssemblyMinutes = request.AssemblyMinutes
        };

        db.Vehicles.Add(vehicle);
        audit.Record(tenantId, "vehicle.created", "Vehicle", vehicle.Id.ToString(),
            new { vehicle.Code, Type = vehicle.Type.ToString(), vehicle.PlantId, vehicle.AssemblyMinutes });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Vehicles.AsNoTracking().Where(v => v.Id == vehicle.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = vehicle.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, VehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null) return NotFound();

        var (code, plate) = Normalize(request);

        // Plant and driver are only re-checked when they change, so renaming a vehicle whose
        // driver was deactivated later still works.
        var problem = await CheckAsync(request, code, plate, id,
            checkPlant: request.PlantId != vehicle.PlantId,
            checkDriver: request.DefaultDriverId != vehicle.DefaultDriverId,
            cancellationToken);
        if (problem is not null) return problem;

        vehicle.PlantId = request.PlantId;
        vehicle.Code = code;
        vehicle.Type = request.Type;
        vehicle.CapacityCft = request.CapacityCft;
        vehicle.PlateNumber = plate;
        vehicle.DefaultDriverId = request.DefaultDriverId;
        vehicle.Status = request.Status;
        vehicle.AssemblyMinutes = request.AssemblyMinutes;
        audit.Record(vehicle.TenantId, "vehicle.updated", "Vehicle", vehicle.Id.ToString(),
            new
            {
                vehicle.Code,
                Status = vehicle.Status.ToString(),
                vehicle.PlantId,
                vehicle.DefaultDriverId,
                vehicle.AssemblyMinutes
            });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static (string Code, string? Plate) Normalize(VehicleRequest request)
    {
        var plate = string.IsNullOrWhiteSpace(request.PlateNumber) ? null : request.PlateNumber.Trim().ToUpperInvariant();
        return (request.Code.Trim().ToUpperInvariant(), plate);
    }

    private async Task<ObjectResult?> CheckAsync(
        VehicleRequest request, string code, string? plate, Guid? selfId,
        bool checkPlant, bool checkDriver, CancellationToken cancellationToken)
    {
        if (request.Type == VehicleType.TransitMixer && request.CapacityCft is null)
        {
            return Problem(title: "A transit mixer needs a capacity in CFT.", statusCode: StatusCodes.Status400BadRequest);
        }

        // The tenant filter means a plant or driver of another company is simply not found.
        if (checkPlant && !await db.Plants.AnyAsync(
                p => p.Id == request.PlantId && p.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (checkDriver && request.DefaultDriverId is { } driverId && !await db.Drivers.AnyAsync(
                d => d.Id == driverId && d.Status == ActiveStatus.Active, cancellationToken))
        {
            return Problem(title: "Choose an active driver of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.Vehicles.AnyAsync(v => v.Code == code && v.Id != selfId, cancellationToken))
        {
            return Problem(title: "A vehicle with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        if (plate is not null && await db.Vehicles.AnyAsync(v => v.PlateNumber == plate && v.Id != selfId, cancellationToken))
        {
            return Problem(title: "A vehicle with that plate number already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
}