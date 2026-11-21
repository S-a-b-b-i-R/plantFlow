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
    Guid? SalesRepId, string? SalesRepName, ActiveStatus Status, int SiteCount, bool VatExempt,
    bool IsOpen, DateTime? OpensAtUtc);

// Mine = owned by the signed-in rep and still within its days. Open = no owner, or the owner's days ran out.
public enum CustomerOwnershipFilter { Mine, Open }

// SalesRepId empty releases the customer, so it is open to every rep.
public class CustomerOwnerRequest
{
    public Guid? SalesRepId { get; init; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

// Customers that opened up since the rep last looked at the open list.
public record OpenedCustomersDto(int Count);

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

public class CustomerVatExemptRequest
{
    public bool VatExempt { get; init; }
}

// Orders that are not completed, rejected or cancelled. Shown as a warning before deactivating.
public record OpenOrdersDto(int OpenOrders);

[ApiController]
[Route("api/customers")]
public class CustomersController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, CustomerOwnership ownership) : ControllerBase
{
    private static Expression<Func<Customer, CustomerDto>> ToDto(CustomerOwnership.Scope scope)
    {
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        return c => new CustomerDto(
            c.Id, c.Code, c.Name, c.Type, c.Phone, c.Email, c.SalesRepId,
            c.SalesRep != null ? c.SalesRep.Name : null, c.Status, c.Sites.Count(), c.VatExempt,
            c.SalesRepId == null || (days > 0 && c.OwnershipStartedUtc <= lapsedFrom),
            c.SalesRepId != null && days > 0 ? c.OwnershipStartedUtc.AddDays(days) : null);
    }

    // ?search= matches code, name or phone.  ?status=  ?type=  ?salesRepId=  ?ownership=Mine|Open
    // ?sort=code|name, "-" for descending. A sales rep only gets their own customers, the open ones,
    // and those where an order of theirs is still under way.
    [HttpGet]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] ActiveStatus? status, [FromQuery] CustomerType? type,
        [FromQuery] Guid? salesRepId, [FromQuery(Name = "ownership")] CustomerOwnershipFilter? filter, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var query = ownership.Visible(db.Customers.AsNoTracking(), scope);
        if (filter == CustomerOwnershipFilter.Open) query = CustomerOwnership.Open(query, scope);
        if (filter == CustomerOwnershipFilter.Mine) query = CustomerOwnership.Mine(query, scope);

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

        return await query.Select(ToDto(scope)).ToPagedAsync(paging, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var customer = await ownership.Visible(db.Customers.AsNoTracking(), scope)
            .Where(c => c.Id == id).Select(ToDto(scope)).FirstOrDefaultAsync(cancellationToken);
        if (customer is null) return NotFound();
        return customer;
    }

    // For the menu badge: how many customers opened up since this rep last looked. Always 0 for
    // managers and for logins without a linked rep.
    [HttpGet("opened")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<OpenedCustomersDto>> Opened(CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        if (!scope.Restricted || scope.RepId is null) return new OpenedCustomersDto(0);

        var seen = await db.SalesReps.Where(r => r.Id == scope.RepId)
            .Select(r => r.OpenCustomersSeenUtc).FirstAsync(cancellationToken);
        return new OpenedCustomersDto(
            await CustomerOwnership.OpenedSinceAsync(db.Customers.AsNoTracking(), scope, seen, cancellationToken));
    }

    // The rep has looked at the open customers, which clears the badge. A view marker, so not audited.
    [HttpPost("opened/seen")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> MarkOpenedSeen(CancellationToken cancellationToken)
    {
        var repId = await ownership.MyRepIdAsync(cancellationToken);
        if (repId is null) return NoContent();

        var rep = await db.SalesReps.FirstAsync(r => r.Id == repId, cancellationToken);
        rep.OpenCustomersSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public const string NoRepLink =
        "Your login is not linked to a sales rep. Ask a sales manager to link it before you add customers.";
    public const string RepChangeRefused = "Only a sales manager can change a customer's sales rep.";
    public const string UseChangeOwner = "Change the sales rep with Change owner, which asks for a reason.";
    public const string VatRefused = "Only Accounts can change whether a customer is VAT exempt.";

    private bool Can(string permission) => User.HasClaim(PlantFlowClaims.Permission, permission);

    [HttpGet("{id:guid}/usage")]
    [HasPermission(Permissions.MasterDataView)]
    public async Task<ActionResult<OpenOrdersDto>> Usage(Guid id, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        if (!await ownership.CanSeeAsync(id, scope, cancellationToken)) return NotFound();

        var open = await db.Orders.CountAsync(
            o => o.CustomerId == id && !PlantUsage.ClosedStatuses.Contains(o.Status), cancellationToken);
        return new OpenOrdersDto(open);
    }

    // A sales rep's new customer is always their own. Someone who assigns reps (a sales manager)
    // picks the rep, or leaves it empty.
    [HttpPost]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var code = request.Code.Trim().ToUpperInvariant();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        var duplicate = await db.Customers.AsNoTracking()
            .Where(c => c.Code == code || (phone != null && c.Phone == phone))
            .Select(c => new
            {
                c.Code, c.Name, c.SalesRepId, Rep = c.SalesRep != null ? c.SalesRep.Name : null,
                IsOpen = c.SalesRepId == null || (days > 0 && c.OwnershipStartedUtc <= lapsedFrom)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
        {
            return Problem(title: DuplicateMessage(duplicate.Code == code, duplicate.Code, duplicate.Name,
                    duplicate.IsOpen, duplicate.SalesRepId == scope.RepId ? null : duplicate.Rep),
                statusCode: StatusCodes.Status409Conflict);
        }

        if (request.VatExempt && !Can(Permissions.CustomersVat))
        {
            return Problem(title: VatRefused, statusCode: StatusCodes.Status403Forbidden);
        }

        Guid? salesRepId;
        if (Can(Permissions.CustomersAssign))
        {
            var problem = await CheckSalesRepAsync(request.SalesRepId, cancellationToken);
            if (problem is not null) return problem;
            salesRepId = request.SalesRepId;
        }
        else
        {
            salesRepId = scope.RepId;
            if (salesRepId is null) return Problem(title: NoRepLink, statusCode: StatusCodes.Status403Forbidden);
        }

        var tenantId = currentUser.TenantId!.Value;
        var customer = new Customer
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            Phone = phone,
            Email = request.Email?.Trim(),
            SalesRepId = salesRepId,
            OwnershipStartedUtc = DateTime.UtcNow,
            Status = request.Status,
            VatExempt = request.VatExempt
        };

        db.Customers.Add(customer);
        audit.Record(tenantId, "customer.created", "Customer", customer.Id.ToString(),
            new { customer.Code, customer.Name, customer.SalesRepId, VatExempt = customer.VatExempt });
        await db.SaveChangesAsync(cancellationToken);

        var dto = await db.Customers.AsNoTracking().Where(c => c.Id == customer.Id).Select(ToDto(scope)).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> Update(Guid id, CustomerRequest request, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var customer = await CustomerOwnership.Workable(db.Customers, scope)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return NotFound();

        if (request.SalesRepId != customer.SalesRepId)
        {
            return Can(Permissions.CustomersAssign)
                ? Problem(title: UseChangeOwner, statusCode: StatusCodes.Status409Conflict)
                : Problem(title: RepChangeRefused, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.VatExempt != customer.VatExempt && !Can(Permissions.CustomersVat))
        {
            return Problem(title: VatRefused, statusCode: StatusCodes.Status403Forbidden);
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            return Problem(title: "A customer with that code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        customer.Code = code;
        customer.Name = request.Name.Trim();
        customer.Type = request.Type;
        customer.Phone = request.Phone?.Trim();
        customer.Email = request.Email?.Trim();
        customer.Status = request.Status;
        customer.VatExempt = request.VatExempt;

        audit.Record(customer.TenantId, "customer.updated", "Customer", customer.Id.ToString(),
            new
            {
                customer.Code, customer.Name, customer.SalesRepId, Status = customer.Status.ToString(),
                VatExempt = customer.VatExempt
            });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // A manager moves a customer to another rep (whose clock starts now), or releases it to every rep.
    [HttpPut("{id:guid}/owner")]
    [HasPermission(Permissions.CustomersAssign)]
    public async Task<IActionResult> ChangeOwner(Guid id, CustomerOwnerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return NotFound();

        var days = await ownership.DaysAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var lapsed = customer.SalesRepId is not null && days > 0 && customer.OwnershipStartedUtc <= now.AddDays(-days);
        if (request.SalesRepId == customer.SalesRepId && !lapsed)
        {
            return Problem(
                title: request.SalesRepId is null ? AlreadyOpen : AlreadyOwned,
                statusCode: StatusCodes.Status409Conflict);
        }

        var problem = await CheckSalesRepAsync(request.SalesRepId, cancellationToken);
        if (problem is not null) return problem;

        var previous = customer.SalesRepId;
        customer.SalesRepId = request.SalesRepId;
        customer.OwnershipStartedUtc = now;
        audit.Record(customer.TenantId, request.SalesRepId is null ? "customer.released" : "customer.reassigned",
            "Customer", customer.Id.ToString(),
            new { customer.Code, PreviousSalesRepId = previous, customer.SalesRepId, Reason = request.Reason.Trim() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public const string AlreadyOpen = "This customer is already open.";
    public const string AlreadyOwned = "This customer already belongs to that rep.";

    // Tells a rep who tries to add an existing customer where it is: open to everyone, or with which rep.
    public static string DuplicateMessage(bool sameCode, string code, string name, bool isOpen, string? otherRep)
    {
        var title = sameCode
            ? $"A customer with the code {code} already exists: {name}."
            : $"{name} ({code}) already has this phone number.";
        if (isOpen) return title + " It is open, so you can find it under Open customers.";
        return otherRep is null ? title : title + $" It belongs to {otherRep}.";
    }

    // Accounts sets VAT exemption without needing to edit anything else about the customer.
    [HttpPut("{id:guid}/vat-exempt")]
    [HasPermission(Permissions.CustomersVat)]
    public async Task<IActionResult> SetVatExempt(
        Guid id, CustomerVatExemptRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return NotFound();
        if (customer.VatExempt == request.VatExempt) return NoContent();

        customer.VatExempt = request.VatExempt;
        audit.Record(customer.TenantId, "customer.vat_exempt_set", "Customer", customer.Id.ToString(),
            new { customer.Code, customer.VatExempt });
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