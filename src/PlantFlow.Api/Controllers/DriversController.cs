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

public record DriverDto(Guid Id, string Name, string? Phone, ActiveStatus Status);

public class DriverRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [StringLength(30)]
    public string? Phone { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/drivers")]
public class DriversController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Driver, DriverDto>> ToDto = d => new DriverDto(d.Id, d.Name, d.Phone, d.Status);

    // ?search= matches name or phone.  ?status=.  ?sort=name, "-name" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<DriverDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Drivers.AsNoTracking();

        if (status is not null) query = query.Where(d => d.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(d => d.Name.Contains(search) || (d.Phone != null && d.Phone.Contains(search)));
        }

        query = paging.Sort?.ToLowerInvariant() == "-name" ? query.OrderByDescending(d => d.Name) : query.OrderBy(d => d.Name);

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<DriverDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.AsNoTracking().Where(d => d.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (driver is null) return NotFound();
        return driver;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<DriverDto>> Create(DriverRequest request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        var driver = new Driver
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Status = request.Status
        };

        db.Drivers.Add(driver);
        audit.Record(tenantId, "driver.created", "Driver", driver.Id.ToString(), new { driver.Name });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Drivers.AsNoTracking().Where(d => d.Id == driver.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = driver.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, DriverRequest request, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver is null) return NotFound();

        driver.Name = request.Name.Trim();
        driver.Phone = request.Phone?.Trim();
        driver.Status = request.Status;

        audit.Record(driver.TenantId, "driver.updated", "Driver", driver.Id.ToString(),
            new { driver.Name, Status = driver.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}