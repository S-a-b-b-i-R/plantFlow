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

public record CustomerDto(
    Guid Id, string Code, string Name, CustomerType Type, string? Phone, string? Email,
    Guid? SalesRepId, string? SalesRepName, ActiveStatus Status, int SiteCount, bool VatExempt);

public class CustomerRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{1,20}$")]
    public string Code { get; init; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    public CustomerType Type { get; init; } = CustomerType.Contractor;

    [StringLength(30)]
    public string? Phone { get; init; }

    [EmailAddress, StringLength(200)]
    public string? Email { get; init; }

    public Guid? SalesRepId { get; init; }

    public ActiveStatus Status { get; init; } = ActiveStatus.Active;
    public bool VatExempt { get; init; }
}

[ApiController]
[Route("api/customers")]
public class CustomersController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static readonly Expression<Func<Customer, CustomerDto>> ToDto = c => new CustomerDto(
        c.Id, c.Code, c.Name, c.Type, c.Phone, c.Email, c.SalesRepId,
        c.SalesRep != null ? c.SalesRep.Name : null, c.Status, c.Sites.Count(), c.VatExempt);

    // ?search= matches code, name or phone.  ?status=  ?type=  ?salesRepId=  ?sort=code|name, "-" for descending.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, [FromQuery] CustomerType? type,
        [FromQuery] Guid? salesRepId, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking();

        if (status is not null) query = query.Where(c => c.Status == status);
        if (type is not null) query = query.Where(c => c.Type == type);
        if (salesRepId is not null) query = query.Where(c => c.SalesRepId == salesRepId);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            query = query.Where(c => c.Code.Contains(search) || c.Name.Contains(search)
                                     || (c.Phone != null && c.Phone.Contains(search)));
        }

        query = paging.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(c => c.Name),
            "-name" => query.OrderByDescending(c => c.Name),
            "-code" => query.OrderByDescending(c => c.Code),
            _ => query.OrderBy(c => c.Code)
        };

        return await query.Select(ToDto).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken);
        if (customer is null) return NotFound();
        return customer;
    }

    [HttpPost]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Problem(title: "A customer with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var problem = await CheckSalesRepAsync(request.SalesRepId, cancellationToken);
        if (problem is not null) return problem;

        var tenantId = currentUser.TenantId!.Value;
        var customer = new Customer
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            SalesRepId = request.SalesRepId,
            Status = request.Status,
            VatExempt = request.VatExempt
        };

        db.Customers.Add(customer);
        audit.Record(tenantId, "customer.created", "Customer", customer.Id.ToString(), new { customer.Code, customer.Name, VatExempt = customer.VatExempt });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Customers.AsNoTracking().Where(c => c.Id == customer.Id).Select(ToDto).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterDataEdit)]
    public async Task<IActionResult> Update(Guid id, CustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            return Problem(title: "A customer with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        // Only re-check the rep when it changes, so an already-assigned rep who later goes inactive
        // does not block unrelated edits to the customer.
        if (request.SalesRepId != customer.SalesRepId)
        {
            var problem = await CheckSalesRepAsync(request.SalesRepId, cancellationToken);
            if (problem is not null) return problem;
        }

        customer.Code = code;
        customer.Name = request.Name.Trim();
        customer.Type = request.Type;
        customer.Phone = request.Phone?.Trim();
        customer.Email = request.Email?.Trim();
        customer.SalesRepId = request.SalesRepId;
        customer.Status = request.Status;
        customer.VatExempt = request.VatExempt;

        audit.Record(customer.TenantId, "customer.updated", "Customer", customer.Id.ToString(),
            new { customer.Code, customer.Name, Status = customer.Status.ToString(), VatExempt = customer.VatExempt });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> CheckSalesRepAsync(Guid? salesRepId, CancellationToken cancellationToken)
    {
        if (salesRepId is null) return null;

        var isActiveRepOfThisCompany = await db.SalesReps.AnyAsync(
            r => r.Id == salesRepId && r.Status == ActiveStatus.Active, cancellationToken);

        return isActiveRepOfThisCompany
            ? null
            : Problem(title: "Choose an active sales rep of this company.", statusCode: StatusCodes.Status400BadRequest);
    }
}