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

public record MaterialDto(Guid Id, string Code, string Name, MaterialCategory Category, MaterialUnit Unit, ActiveStatus Status);

public class MaterialRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Nullable + Required, so a missing value is rejected instead of silently becoming Cement.
    [Required]
    public MaterialCategory? Category { get; init; }

    public MaterialUnit Unit { get; init; } = MaterialUnit.Kg;

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/materials")]
public class MaterialsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Material, MaterialDto>> ToDto = m =>
        new MaterialDto(m.Id, m.Code, m.Name, m.Category, m.Unit, m.Status);

    // ?search= matches code or name.  ?category=  ?status=  ?sort=code|name, "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<MaterialDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] MaterialCategory? category, [FromQuery] ActiveStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Materials.AsNoTracking();

        if (category is not null) query = query.Where(m => m.Category == category);
        if (status is not null) query = query.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(m => m.Code.Contains(search) || m.Name.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(m => m.Name),
            "-name" => query.OrderByDescending(m => m.Name),
            "-code" => query.OrderByDescending(m => m.Code),
            _ => query.OrderBy(m => m.Code)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<MaterialDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var material = await db.Materials.AsNoTracking().Where(m => m.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (material is null) return NotFound();
        return material;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<MaterialDto>> Create(MaterialRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Materials.AnyAsync(m => m.Code == code, cancellationToken))
        {
            return Problem(title: "A material with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = currentUser.TenantId!.Value;
        var material = new Material
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Category = request.Category!.Value,
            Unit = request.Unit,
            Status = request.Status
        };

        db.Materials.Add(material);
        audit.Record(tenantId, "material.created", "Material", material.Id.ToString(),
            new { material.Code, material.Name, Category = material.Category.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Materials.AsNoTracking().Where(m => m.Id == material.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = material.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, MaterialRequest request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (material is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Materials.AnyAsync(m => m.Code == code && m.Id != id, cancellationToken))
        {
            return Problem(title: "A material with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        material.Code = code;
        material.Name = request.Name.Trim();
        material.Category = request.Category!.Value;
        material.Unit = request.Unit;
        material.Status = request.Status;

        audit.Record(material.TenantId, "material.updated", "Material", material.Id.ToString(),
            new { material.Code, material.Name, Status = material.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}