using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.Orders;

public enum ApprovalStage { SalesManager, Management, Technical, Credit }

// Withdrawn: no longer needed, or restarted because the BOQ was revised.
public enum ApprovalDecision { Pending, Approved, Rejected, Withdrawn }

public class OrderApproval : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    // The submission round this approval belongs to.
    public int Round { get; set; }

    public ApprovalStage Stage { get; set; }
    public ApprovalDecision Decision { get; set; } = ApprovalDecision.Pending;

    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? Comment { get; set; }
}