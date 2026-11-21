using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Ledger;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// A customer with credits. OpenCft and OpenValue count what can still be used; PendingCloseCft is held for a close.
public record LedgerCustomerDto(
    Guid CustomerId, string Code, string Name, decimal OpenCft, decimal OpenValue, decimal PendingCloseCft, int Credits);

public enum LedgerEntryKind { Created, Used, Corrected, Closed }

// One line of a credit's history. QuantityCft is signed: + adds to the credit, - takes from it.
// A close carries its status; a sent-back close takes nothing.
public record LedgerEntryDto(
    LedgerEntryKind Kind, DateTime AtUtc, decimal QuantityCft, string? Text, Guid? OrderId, string? OrderNumber,
    LedgerCloseStatus? CloseStatus, string? By);

public record LedgerCreditDto(
    Guid Id, Guid ProductId, string ProductCode, string ProductName, decimal UnitPrice, LedgerCreditSource Source,
    Guid? SourceOrderId, string? SourceOrderNumber, decimal OriginalCft, decimal BalanceCft, decimal PendingCloseCft,
    string? Note, DateTime CreatedAtUtc, List<LedgerEntryDto> Entries);

// Per order of the customer: what was ordered, delivered and billed. BilledCft is final once the order is
// closed (Final) and provisional before that.
public record LedgerOrderDto(
    Guid OrderId, string Number, OrderStatus Status, DateOnly RequestedDate, DateTime? ClosedAtUtc, bool Final,
    decimal OrderedCft, decimal DeliveredCft, decimal BilledCft, decimal CreditUsedCft, decimal CarriedForwardCft);

public record LedgerStatementDto(
    Guid CustomerId, string Code, string Name, decimal OpenCft, decimal OpenValue,
    List<LedgerCreditDto> Credits, PagedResult<LedgerOrderDto> Orders);

public record LedgerCloseDto(
    Guid Id, Guid CreditId, Guid CustomerId, string CustomerName, string ProductCode, decimal UnitPrice,
    decimal QuantityCft, decimal Value, LedgerCloseReason Reason, string Note, LedgerCloseStatus Status,
    string? RequestedByName, DateTime RequestedAtUtc, bool RequestedByMe,
    string? ReviewedByName, DateTime? ReviewedAtUtc, string? ReviewComment);

public class ManualCreditRequest
{
    [Required]
    public Guid? ProductId { get; init; }

    // At most 2 decimals.
    [Range(0.01, 1000000.0)]
    public decimal QuantityCft { get; init; }

    // Per CFT, excluding VAT, at most 2 decimals.
    [Range(0.0, 10000000.0)]
    public decimal UnitPrice { get; init; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

public class LedgerCorrectionRequest
{
    // Positive adds, negative takes away. Not zero, at most 2 decimals.
    [Range(-1000000.0, 1000000.0)]
    public decimal QuantityCft { get; init; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

public class LedgerCloseRequest
{
    // Empty closes everything that is left.
    [Range(0.01, 1000000.0)]
    public decimal? QuantityCft { get; init; }

    [Required]
    public LedgerCloseReason? Reason { get; init; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Note { get; init; } = "";
}

public class LedgerSendBackRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
public class LedgerController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, CustomerOwnership ownership) : ControllerBase
{
    private static readonly OrderStatus[] StatementStatuses =
        [OrderStatus.Confirmed, OrderStatus.InProgress, OrderStatus.OnHold, OrderStatus.Completed];

    // Customers with credits. ?all=true also lists customers whose credits are used up or closed.
    [HttpGet("api/ledger")]
    [HasPermission(Permissions.LedgerView)]
    public async Task<ActionResult<PagedResult<LedgerCustomerDto>>> List(
        [FromQuery] PageQuery paging, [FromQuery] bool all, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var customers = ownership.Visible(db.Customers.AsNoTracking(), scope)
            .Where(c => db.LedgerCredits.Any(l => l.CustomerId == c.Id));

        if (!all)
        {
            customers = customers.Where(c => db.LedgerCredits.Any(l => l.CustomerId == c.Id
                && (l.BalanceCft > 0 || l.Closes.Any(x => x.Status == LedgerCloseStatus.Pending))));
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var search = paging.Search.Trim();
            customers = customers.Where(c => c.Name.Contains(search) || c.Code.Contains(search));
        }

        return await customers.OrderBy(c => c.Name)
            .Select(c => new LedgerCustomerDto(
                c.Id, c.Code, c.Name,
                db.LedgerCredits.Where(l => l.CustomerId == c.Id).Sum(l => (decimal?)l.BalanceCft) ?? 0m,
                db.LedgerCredits.Where(l => l.CustomerId == c.Id).Sum(l => (decimal?)(l.BalanceCft * l.UnitPrice)) ?? 0m,
                db.LedgerCloses.Where(x => x.Credit.CustomerId == c.Id && x.Status == LedgerCloseStatus.Pending)
                    .Sum(x => (decimal?)x.QuantityCft) ?? 0m,
                db.LedgerCredits.Count(l => l.CustomerId == c.Id)))
            .ToPagedAsync(paging, cancellationToken);
    }

    // The customer's statement: every credit with its history, and their orders (newest first, paged).
    [HttpGet("api/customers/{id:guid}/ledger")]
    [HasPermission(Permissions.LedgerView)]
    public async Task<ActionResult<LedgerStatementDto>> Statement(
        Guid id, [FromQuery] PageQuery paging, CancellationToken cancellationToken)
    {
        var scope = await ownership.ScopeAsync(User, cancellationToken);
        var customer = await ownership.Visible(db.Customers.AsNoTracking(), scope)
            .Where(c => c.Id == id).Select(c => new { c.Id, c.Code, c.Name }).FirstOrDefaultAsync(cancellationToken);
        if (customer is null) return NotFound();

        var credits = await CreditsAsync(id, cancellationToken);

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id && StatementStatuses.Contains(o.Status))
            .OrderByDescending(o => o.Sequence)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.RequestedDate,
                o.ClosedAtUtc,
                Ordered = o.Lines.Sum(l => l.QuantityCft),
                Delivered = o.Lines.Sum(l => l.DeliveredCft),
                Lines = o.Lines.Select(l => new { l.QuantityCft, l.DeliveredCft, l.BilledCft }).ToList(),
                Used = db.LedgerCreditUses.Where(u => u.OrderId == o.Id).Sum(u => (decimal?)u.QuantityCft) ?? 0m,
                Carried = db.LedgerCredits.Where(c => c.SourceOrderId == o.Id).Sum(c => (decimal?)c.OriginalCft) ?? 0m
            })
            .ToPagedAsync(paging, cancellationToken);

        var orderRows = orders.Items.Select(o => new LedgerOrderDto(
            o.Id, o.Number, o.Status, o.RequestedDate, o.ClosedAtUtc, o.ClosedAtUtc is not null,
            o.Ordered, o.Delivered,
            o.Lines.Sum(l => CustomerLedger.EffectiveBilled(l.QuantityCft, l.DeliveredCft, l.BilledCft)),
            o.Used, o.Carried)).ToList();

        return new LedgerStatementDto(
            customer.Id, customer.Code, customer.Name,
            credits.Sum(c => c.BalanceCft), credits.Sum(c => Math.Round(c.BalanceCft * c.UnitPrice, 2)),
            credits, new PagedResult<LedgerOrderDto>(orderRows, orders.Page, orders.PageSize, orders.TotalCount));
    }

    // A credit entered by Accounts, for example a balance brought over from the company's old records.
    [HttpPost("api/customers/{id:guid}/ledger/credits")]
    [HasPermission(Permissions.LedgerManage)]
    public async Task<ActionResult<LedgerCreditDto>> AddCredit(
        Guid id, ManualCreditRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return NotFound();

        if (request.QuantityCft != Math.Round(request.QuantityCft, 2) || request.UnitPrice != Math.Round(request.UnitPrice, 2))
        {
            return Problem(title: "Quantities and prices can have at most 2 decimal places.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
        {
            return Problem(title: "Choose a product from the catalogue.", statusCode: StatusCodes.Status400BadRequest);
        }

        var credit = new LedgerCredit
        {
            TenantId = customer.TenantId,
            CustomerId = customer.Id,
            ProductId = request.ProductId!.Value,
            UnitPrice = request.UnitPrice,
            Source = LedgerCreditSource.Manual,
            OriginalCft = request.QuantityCft,
            BalanceCft = request.QuantityCft,
            Note = request.Reason.Trim(),
            CreatedBy = currentUser.UserId
        };
        db.LedgerCredits.Add(credit);

        audit.Record(customer.TenantId, "ledger.credit_added", "LedgerCredit", credit.Id.ToString(),
            new { Customer = customer.Code, credit.ProductId, credit.OriginalCft, credit.UnitPrice, credit.Note });
        await db.SaveChangesAsync(cancellationToken);

        var dto = (await CreditsAsync(id, cancellationToken)).Single(c => c.Id == credit.Id);
        return Created($"api/customers/{id}/ledger", dto);
    }

    [HttpPost("api/ledger/credits/{id:guid}/corrections")]
    [HasPermission(Permissions.LedgerManage)]
    public async Task<IActionResult> Correct(Guid id, LedgerCorrectionRequest request, CancellationToken cancellationToken)
    {
        var credit = await db.LedgerCredits.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (credit is null) return NotFound();

        if (request.QuantityCft == 0 || request.QuantityCft != Math.Round(request.QuantityCft, 2))
        {
            return Problem(title: "Enter the CFT to add or take away, with at most 2 decimal places.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (credit.BalanceCft + request.QuantityCft < 0)
        {
            return Problem(title: $"Only {credit.BalanceCft:0.##} CFT is left on this credit to take away.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.LedgerCorrections.Add(new LedgerCorrection
        {
            TenantId = credit.TenantId,
            CreditId = credit.Id,
            QuantityCft = request.QuantityCft,
            Reason = request.Reason.Trim(),
            CreatedBy = currentUser.UserId
        });
        credit.BalanceCft += request.QuantityCft;

        audit.Record(credit.TenantId, "ledger.credit_corrected", "LedgerCredit", credit.Id.ToString(),
            new { request.QuantityCft, Reason = request.Reason.Trim(), credit.BalanceCft });
        return await SaveAsync(cancellationToken) ?? NoContent();
    }

    // Asks to close volume the customer no longer needs. It is held back from use until someone else decides.
    [HttpPost("api/ledger/credits/{id:guid}/closes")]
    [HasPermission(Permissions.LedgerManage)]
    public async Task<IActionResult> RequestClose(Guid id, LedgerCloseRequest request, CancellationToken cancellationToken)
    {
        var credit = await db.LedgerCredits.Include(c => c.Closes).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (credit is null) return NotFound();

        if (credit.Closes.Any(c => c.Status == LedgerCloseStatus.Pending))
        {
            return Problem(title: "This credit already has a close waiting for approval.", statusCode: StatusCodes.Status409Conflict);
        }

        if (credit.BalanceCft <= 0)
        {
            return Problem(title: "Nothing is left on this credit to close.", statusCode: StatusCodes.Status409Conflict);
        }

        var quantity = request.QuantityCft ?? credit.BalanceCft;
        if (quantity != Math.Round(quantity, 2))
        {
            return Problem(title: "Quantities can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (quantity > credit.BalanceCft)
        {
            return Problem(title: $"Only {credit.BalanceCft:0.##} CFT is left on this credit to close.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var close = new LedgerClose
        {
            TenantId = credit.TenantId,
            CreditId = credit.Id,
            QuantityCft = quantity,
            Reason = request.Reason!.Value,
            Note = request.Note.Trim(),
            RequestedBy = currentUser.UserId
        };
        db.LedgerCloses.Add(close);
        credit.BalanceCft -= quantity;

        audit.Record(credit.TenantId, "ledger.close_requested", "LedgerCredit", credit.Id.ToString(),
            new { CloseId = close.Id, QuantityCft = quantity, Reason = close.Reason.ToString(), close.Note });
        return await SaveAsync(cancellationToken) ?? NoContent();
    }

    // Closes asked for, newest first. ?status=Pending is the approval inbox.
    [HttpGet("api/ledger/closes")]
    [HasPermission(Permissions.LedgerView, Permissions.LedgerApprove)]
    public async Task<ActionResult<List<LedgerCloseDto>>> Closes(
        [FromQuery] LedgerCloseStatus? status, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var query = db.LedgerCloses.AsNoTracking();
        if (status is not null) query = query.Where(c => c.Status == status);

        return await query.OrderByDescending(c => c.RequestedAtUtc).Take(200)
            .Select(c => new LedgerCloseDto(
                c.Id, c.CreditId, c.Credit.CustomerId, c.Credit.Customer.Name, c.Credit.Product.Code, c.Credit.UnitPrice,
                c.QuantityCft, Math.Round(c.QuantityCft * c.Credit.UnitPrice, 2), c.Reason, c.Note, c.Status,
                db.Users.Where(u => u.Id == c.RequestedBy).Select(u => u.DisplayName ?? u.Email).FirstOrDefault(),
                c.RequestedAtUtc, c.RequestedBy == me,
                db.Users.Where(u => u.Id == c.ReviewedBy).Select(u => u.DisplayName ?? u.Email).FirstOrDefault(),
                c.ReviewedAtUtc, c.ReviewComment))
            .ToListAsync(cancellationToken);
    }

    [HttpPost("api/ledger/closes/{id:guid}/approve")]
    [HasPermission(Permissions.LedgerApprove)]
    public async Task<IActionResult> ApproveClose(Guid id, CancellationToken cancellationToken)
    {
        var (close, problem) = await PendingCloseAsync(id, cancellationToken);
        if (problem is not null) return problem;

        close!.Status = LedgerCloseStatus.Approved;
        close.ReviewedBy = currentUser.UserId;
        close.ReviewedAtUtc = DateTime.UtcNow;

        audit.Record(close.TenantId, "ledger.close_approved", "LedgerCredit", close.CreditId.ToString(),
            new { CloseId = close.Id, close.QuantityCft, Reason = close.Reason.ToString() });
        return await SaveAsync(cancellationToken) ?? NoContent();
    }

    // The held volume goes back on the credit.
    [HttpPost("api/ledger/closes/{id:guid}/send-back")]
    [HasPermission(Permissions.LedgerApprove)]
    public async Task<IActionResult> SendBackClose(Guid id, LedgerSendBackRequest request, CancellationToken cancellationToken)
    {
        var (close, problem) = await PendingCloseAsync(id, cancellationToken);
        if (problem is not null) return problem;

        close!.Status = LedgerCloseStatus.SentBack;
        close.ReviewedBy = currentUser.UserId;
        close.ReviewedAtUtc = DateTime.UtcNow;
        close.ReviewComment = request.Reason.Trim();
        close.Credit.BalanceCft += close.QuantityCft;

        audit.Record(close.TenantId, "ledger.close_sent_back", "LedgerCredit", close.CreditId.ToString(),
            new { CloseId = close.Id, close.QuantityCft, close.ReviewComment, close.Credit.BalanceCft });
        return await SaveAsync(cancellationToken) ?? NoContent();
    }

    private async Task<(LedgerClose?, IActionResult?)> PendingCloseAsync(Guid id, CancellationToken cancellationToken)
    {
        var close = await db.LedgerCloses.Include(c => c.Credit).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (close is null) return (null, NotFound());

        if (close.Status != LedgerCloseStatus.Pending)
        {
            return (null, Problem(title: "This close has already been decided.", statusCode: StatusCodes.Status409Conflict));
        }

        if (close.RequestedBy == currentUser.UserId)
        {
            return (null, Problem(title: "You asked for this close, so someone else has to decide it.",
                statusCode: StatusCodes.Status409Conflict));
        }

        return (close, null);
    }

    private async Task<IActionResult?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(title: CustomerLedger.ChangedMessage, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private async Task<List<LedgerCreditDto>> CreditsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var credits = await db.LedgerCredits.AsNoTracking().Where(c => c.CustomerId == customerId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.ProductId,
                c.Product.Code,
                c.Product.Name,
                c.UnitPrice,
                c.Source,
                c.SourceOrderId,
                SourceOrderNumber = c.SourceOrder != null ? c.SourceOrder.Number : null,
                c.OriginalCft,
                c.BalanceCft,
                c.Note,
                c.CreatedAtUtc,
                c.CreatedBy,
                Uses = c.Uses.Select(u => new { u.CreatedAtUtc, u.QuantityCft, u.OrderId, u.Order.Number, u.CreatedBy }).ToList(),
                Corrections = c.Corrections.Select(x => new { x.CreatedAtUtc, x.QuantityCft, x.Reason, x.CreatedBy }).ToList(),
                Closes = c.Closes.Select(x => new
                {
                    x.RequestedAtUtc, x.QuantityCft, x.Reason, x.Note, x.Status, x.RequestedBy, x.ReviewComment
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var userIds = credits.SelectMany(c => c.Uses.Select(u => u.CreatedBy)
                .Concat(c.Corrections.Select(x => x.CreatedBy))
                .Concat(c.Closes.Select(x => x.RequestedBy))
                .Append(c.CreatedBy))
            .OfType<Guid>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, cancellationToken);
        string? Name(Guid? userId) => userId is { } u ? names.GetValueOrDefault(u) : null;

        return credits.Select(c =>
        {
            var entries = new List<LedgerEntryDto>
            {
                new(LedgerEntryKind.Created, c.CreatedAtUtc, c.OriginalCft, c.Note, c.SourceOrderId, c.SourceOrderNumber,
                    null, Name(c.CreatedBy))
            };
            entries.AddRange(c.Uses.Select(u => new LedgerEntryDto(
                LedgerEntryKind.Used, u.CreatedAtUtc, -u.QuantityCft, null, u.OrderId, u.Number, null, Name(u.CreatedBy))));
            entries.AddRange(c.Corrections.Select(x => new LedgerEntryDto(
                LedgerEntryKind.Corrected, x.CreatedAtUtc, x.QuantityCft, x.Reason, null, null, null, Name(x.CreatedBy))));
            entries.AddRange(c.Closes.Select(x => new LedgerEntryDto(
                LedgerEntryKind.Closed, x.RequestedAtUtc, x.Status == LedgerCloseStatus.SentBack ? 0m : -x.QuantityCft,
                x.Status == LedgerCloseStatus.SentBack
                    ? $"{CloseReasonText(x.Reason)}: {x.Note}. Sent back: {x.ReviewComment}"
                    : $"{CloseReasonText(x.Reason)}: {x.Note}",
                null, null, x.Status, Name(x.RequestedBy))));

            var pending = c.Closes.Where(x => x.Status == LedgerCloseStatus.Pending).Sum(x => x.QuantityCft);
            return new LedgerCreditDto(
                c.Id, c.ProductId, c.Code, c.Name, c.UnitPrice, c.Source, c.SourceOrderId, c.SourceOrderNumber,
                c.OriginalCft, c.BalanceCft, pending, c.Note, c.CreatedAtUtc,
                entries.OrderBy(e => e.AtUtc).ToList());
        }).ToList();
    }

    private static string CloseReasonText(LedgerCloseReason reason) => reason switch
    {
        LedgerCloseReason.Refunded => "Refunded",
        _ => "Not needed"
    };
}
