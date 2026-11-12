using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Domain.Pricing;

public enum PriceStatus { Draft, PendingApproval, Active, Expired }

// The selling price per CFT (excluding VAT) of a product, for everyone or for one plant and/or customer.
public class Price : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Empty means every plant.
    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    // Empty means every customer.
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal PricePerCft { get; set; }

    public DateOnly ValidFrom { get; set; }

    // Empty means it has no end date.
    public DateOnly? ValidTo { get; set; }

    public PriceStatus Status { get; set; } = PriceStatus.Draft;

    // Why management sent it back. Cleared when it is submitted again.
    public string? RejectionReason { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}