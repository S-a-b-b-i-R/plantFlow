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

public record ProductDto(
    Guid Id, string Code, string Name, string StrengthClass, int MaxAggregateMm, int DefaultSlumpMm, ActiveStatus Status);

public class ProductRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Free text such as "M25", "C30" or "3000 psi".
    [Required, RegularExpression(@"^[A-Za-z0-9 ./-]{1,20}$")]
    public string StrengthClass { get; init; } = "";

    [Range(5, 80)]
    public int MaxAggregateMm { get; init; }

    [Range(0, 300)]
    public int DefaultSlumpMm { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
}

[ApiController]
[Route("api/products")]
public class ProductsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Product, ProductDto>> ToDto = p =>
        new ProductDto(p.Id, p.Code, p.Name, p.StrengthClass, p.MaxAggregateMm, p.DefaultSlumpMm, p.Status);

    // ?search= matches code, name or strength class.  ?status=  ?sort=code|name|strength, "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        if (status is not null) query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search) || p.StrengthClass.Contains(search));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "-name" => query.OrderByDescending(p => p.Name),
            "strength" => query.OrderBy(p => p.StrengthClass).ThenBy(p => p.MaxAggregateMm),
            "-strength" => query.OrderByDescending(p => p.StrengthClass).ThenBy(p => p.MaxAggregateMm),
            "-code" => query.OrderByDescending(p => p.Code),
            _ => query.OrderBy(p => p.Code)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();
        return product;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return Problem(title: "A product with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = currentUser.TenantId!.Value;
        var product = new Product
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            StrengthClass = request.StrengthClass.Trim().ToUpperInvariant(),
            MaxAggregateMm = request.MaxAggregateMm,
            DefaultSlumpMm = request.DefaultSlumpMm,
            Status = request.Status
        };

        db.Products.Add(product);
        audit.Record(tenantId, "product.created", "Product", product.Id.ToString(),
            new { product.Code, product.Name, product.StrengthClass });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Products.AsNoTracking().Where(p => p.Id == product.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Code == code && p.Id != id, cancellationToken))
        {
            return Problem(title: "A product with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        product.Code = code;
        product.Name = request.Name.Trim();
        product.StrengthClass = request.StrengthClass.Trim().ToUpperInvariant();
        product.MaxAggregateMm = request.MaxAggregateMm;
        product.DefaultSlumpMm = request.DefaultSlumpMm;
        product.Status = request.Status;

        audit.Record(product.TenantId, "product.updated", "Product", product.Id.ToString(),
            new { product.Code, product.Name, Status = product.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}