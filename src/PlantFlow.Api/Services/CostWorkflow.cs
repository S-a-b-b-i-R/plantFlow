using PlantFlow.Api.Domain.Costing;

namespace PlantFlow.Api.Services;

// The submit, send back, approve and retire steps shared by material costs, overheads and delivery bands.
// Each method returns a plain-language reason when the step is not allowed, and null after it has been applied.
public static class CostWorkflow
{
    public static string Label(CostStatus status) => status switch
    {
        CostStatus.Draft => "draft",
        CostStatus.PendingApproval => "waiting for approval",
        CostStatus.Approved => "approved",
        CostStatus.Retired => "retired",
        _ => status.ToString()
    };

    // An approved or retired row is never edited or deleted. A change is a new row.
    public static string? RequireDraft(ApprovableCost cost, string noun, string verb) =>
        cost.Status == CostStatus.Draft
            ? null
            : $"Only a draft {noun} can be {verb}. This {noun} is {Label(cost.Status)}.";

    public static string? Submit(ApprovableCost cost, string noun) =>
        Move(cost, CostStatus.Draft, CostStatus.PendingApproval, "submitted", noun, () => cost.RejectionReason = null);

    public static string? Reject(ApprovableCost cost, string reason, string noun) =>
        Move(cost, CostStatus.PendingApproval, CostStatus.Draft, "sent back", noun, () => cost.RejectionReason = reason.Trim());

    public static string? Retire(ApprovableCost cost, string noun) =>
        Move(cost, CostStatus.Approved, CostStatus.Retired, "retired", noun, () => { });

    // Checks the status only. The caller runs its own checks, then calls MarkApproved.
    public static string? RequirePending(ApprovableCost cost, string noun) =>
        cost.Status == CostStatus.PendingApproval
            ? null
            : $"Only a {noun} waiting for approval can be approved. This {noun} is {Label(cost.Status)}.";

    public static void MarkApproved(ApprovableCost cost, Guid? userId)
    {
        cost.Status = CostStatus.Approved;
        cost.RejectionReason = null;
        cost.ApprovedBy = userId;
        cost.ApprovedAtUtc = DateTime.UtcNow;
    }

    private static string? Move(
        ApprovableCost cost, CostStatus from, CostStatus to, string verb, string noun, Action apply)
    {
        if (cost.Status != from)
        {
            return $"Only a {Label(from)} {noun} can be {verb}. This {noun} is {Label(cost.Status)}.";
        }

        cost.Status = to;
        apply();
        return null;
    }
}
