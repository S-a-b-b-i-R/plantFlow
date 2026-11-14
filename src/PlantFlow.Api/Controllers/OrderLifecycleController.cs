using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public class ConfirmOrderRequest
{
    [Required]
    public ConfirmationMethod? Method { get; init; }

    // For example a PO number.
    [StringLength(100)]
    public string? Reference { get; init; }
}

public class HoldOrderRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; init; } = "";
}

[ApiController]
[Route("api/orders/{id:guid}")]
public class OrderLifecycleController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, OrderApprovalFlow flow) : ControllerBase
{
    // Records the customer's go-ahead. From here quantities and prices are locked except through an amendment.
    [HttpPost("confirm")]
    [HasPermission(Permissions.OrdersConfirm)]
    public async Task<IActionResult> Confirm(Guid id, ConfirmOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Approved)
        {
            return Problem(title: $"Only an approved order can be confirmed. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        order.ConfirmedBy = currentUser.UserId;
        order.ConfirmedAtUtc = DateTime.UtcNow;
        order.ConfirmationMethod = request.Method;
        order.ConfirmationReference = request.Reference?.Trim();
        order.ConfirmedTotal = order.Lines.Sum(l => l.QuantityCft * l.UnitPrice);
        flow.ChangeStatus(order, OrderStatus.Confirmed, null);

        audit.Record(order.TenantId, "order.confirmed", "Order", order.Id.ToString(),
            new { order.Number, Method = request.Method.ToString(), order.ConfirmationReference, order.ConfirmedTotal });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("hold")]
    [HasPermission(Permissions.OrdersConfirm)]
    public async Task<IActionResult> Hold(Guid id, HoldOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProgress))
        {
            return Problem(title: $"Only a confirmed or running order can be put on hold. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        order.HeldFromStatus = order.Status;
        order.HoldReason = request.Reason.Trim();
        flow.ChangeStatus(order, OrderStatus.OnHold, order.HoldReason);

        audit.Record(order.TenantId, "order.held", "Order", order.Id.ToString(),
            new { order.Number, From = order.HeldFromStatus.ToString(), order.HoldReason });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("release")]
    [HasPermission(Permissions.OrdersConfirm)]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.OnHold)
        {
            return Problem(title: $"Only an order on hold can be released. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var to = order.HeldFromStatus ?? OrderStatus.Confirmed;
        order.HeldFromStatus = null;
        order.HoldReason = null;
        flow.ChangeStatus(order, to, null);

        audit.Record(order.TenantId, "order.released", "Order", order.Id.ToString(), new { order.Number, To = to.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}