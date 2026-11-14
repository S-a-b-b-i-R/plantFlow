using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public class ApproveOrderRequest
{
    [StringLength(500)]
    public string? Comment { get; init; }
}

public class RejectOrderRequest
{
    // The reason the order goes back to the rep.
    [Required, StringLength(500, MinimumLength = 3)]
    public string Comment { get; init; } = "";
}

[ApiController]
[Route("api/orders/{id:guid}/approvals")]
public class OrderApprovalsController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, OrderApprovalFlow flow) : ControllerBase
{
    [HttpPost("sales-manager/approve")]
    [HasPermission(Permissions.OrdersApproveSalesManager)]
    public Task<IActionResult> SalesManagerApprove(Guid id, [FromBody] ApproveOrderRequest? request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.SalesManager, true, request?.Comment, cancellationToken);

    [HttpPost("sales-manager/reject")]
    [HasPermission(Permissions.OrdersApproveSalesManager)]
    public Task<IActionResult> SalesManagerReject(Guid id, RejectOrderRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.SalesManager, false, request.Comment, cancellationToken);

    [HttpPost("management/approve")]
    [HasPermission(Permissions.OrdersApproveManagement)]
    public Task<IActionResult> ManagementApprove(Guid id, [FromBody] ApproveOrderRequest? request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.Management, true, request?.Comment, cancellationToken);

    [HttpPost("management/reject")]
    [HasPermission(Permissions.OrdersApproveManagement)]
    public Task<IActionResult> ManagementReject(Guid id, RejectOrderRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.Management, false, request.Comment, cancellationToken);

    [HttpPost("credit/approve")]
    [HasPermission(Permissions.OrdersApproveCredit)]
    public Task<IActionResult> CreditApprove(Guid id, [FromBody] ApproveOrderRequest? request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.Credit, true, request?.Comment, cancellationToken);

    [HttpPost("credit/reject")]
    [HasPermission(Permissions.OrdersApproveCredit)]
    public Task<IActionResult> CreditReject(Guid id, RejectOrderRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, ApprovalStage.Credit, false, request.Comment, cancellationToken);

    private async Task<IActionResult> DecideAsync(
        Guid id, ApprovalStage stage, bool approve, string? comment, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Lines).Include(o => o.Approvals)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status is not (OrderStatus.Submitted or OrderStatus.InApproval))
        {
            return Problem(title: $"Only an order waiting for approval can be decided. This order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var row = flow.Current(order).FirstOrDefault(a => a.Stage == stage && a.Decision == ApprovalDecision.Pending);
        if (row is null)
        {
            return Problem(title: $"There is no {stage} approval waiting on this order.",
                statusCode: StatusCodes.Status409Conflict);
        }

        row.DecidedBy = currentUser.UserId;
        row.DecidedAtUtc = DateTime.UtcNow;
        row.Comment = comment?.Trim();

        if (approve)
        {
            row.Decision = ApprovalDecision.Approved;
            await flow.AdvanceAsync(order, order.Lines, cancellationToken);
        }
        else
        {
            flow.Reject(order, row, row.Comment!);
        }

        audit.Record(order.TenantId, approve ? "order.approved" : "order.rejected", "Order", order.Id.ToString(),
            new { order.Number, Stage = stage.ToString(), Status = order.Status.ToString(), row.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}