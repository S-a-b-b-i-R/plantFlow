using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Services;

// The rules for which approvals an order needs and how its status follows from them.
// The order must be loaded with its Approvals. New rows are added through the DbSets, never the order's lists.
public class OrderApprovalFlow(PlantFlowDbContext db, ICurrentUser currentUser)
{
    public static bool IsBelowFloor(OrderLine line) => line.FloorPrice is not null && line.UnitPrice < line.FloorPrice;

    // The approvals of the current round that still count.
    public List<OrderApproval> Current(Order order) =>
        order.Approvals.Where(a => a.Round == order.ApprovalRound && a.Decision != ApprovalDecision.Withdrawn).ToList();

    // A draft is submitted: a new round starts and only the Sales Manager stage opens.
    public void Start(Order order)
    {
        order.ApprovalRound++;
        OpenStage(order, ApprovalStage.SalesManager);
        ChangeStatus(order, OrderStatus.Submitted, null);
    }

    // Opens the stages that are needed once the Sales Manager has approved, and settles the status.
    public async Task AdvanceAsync(Order order, IEnumerable<OrderLine> lines, CancellationToken cancellationToken)
    {
        var current = Current(order);
        var manager = current.FirstOrDefault(a => a.Stage == ApprovalStage.SalesManager);

        if (manager is null || manager.Decision != ApprovalDecision.Approved)
        {
            ChangeStatus(order, OrderStatus.Submitted, null);
            return;
        }

        var requireCredit = await db.Tenants.Where(t => t.Id == order.TenantId)
            .Select(t => t.RequireCreditApproval).FirstAsync(cancellationToken);

        var required = new List<ApprovalStage>();
        if (lines.Any(IsBelowFloor)) required.Add(ApprovalStage.Management);
        if (requireCredit) required.Add(ApprovalStage.Credit);

        foreach (var stage in required.Where(s => current.All(a => a.Stage != s)))
        {
            OpenStage(order, stage);
        }

        foreach (var obsolete in current.Where(a =>
                     a.Stage != ApprovalStage.SalesManager && a.Decision == ApprovalDecision.Pending
                     && !required.Contains(a.Stage)))
        {
            obsolete.Decision = ApprovalDecision.Withdrawn;
        }

        var allApproved = required.All(stage =>
            current.Any(a => a.Stage == stage && a.Decision == ApprovalDecision.Approved));

        ChangeStatus(order, allApproved ? OrderStatus.Approved : OrderStatus.InApproval, null);
    }

    // A stage rejects: the order goes back to the rep and the other open approvals are withdrawn.
    public void Reject(Order order, OrderApproval row, string comment)
    {
        row.Decision = ApprovalDecision.Rejected;
        WithdrawPending(order);
        ChangeStatus(order, OrderStatus.Draft, comment);
    }

    public void WithdrawPending(Order order)
    {
        foreach (var approval in Current(order).Where(a => a.Decision == ApprovalDecision.Pending))
        {
            approval.Decision = ApprovalDecision.Withdrawn;
        }
    }

    // A revised price, quantity or line restarts Management and Credit. AdvanceAsync opens them again if still needed.
    public void WithdrawCommercialStages(Order order)
    {
        foreach (var approval in Current(order).Where(a => a.Stage is ApprovalStage.Management or ApprovalStage.Credit))
        {
            approval.Decision = ApprovalDecision.Withdrawn;
        }
    }

    public void ChangeStatus(Order order, OrderStatus to, string? reason)
    {
        var from = order.Status;
        if (from == to) return;

        order.Status = to;
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedBy = currentUser.UserId,
            Reason = reason
        });
    }

    private void OpenStage(Order order, ApprovalStage stage) =>
        db.OrderApprovals.Add(new OrderApproval
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            Round = order.ApprovalRound,
            Stage = stage
        });
}