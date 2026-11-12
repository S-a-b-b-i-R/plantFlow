using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Domain.MixDesigns;

public enum MixDesignKind { Standard, Custom }

public enum MixDesignStatus { Draft, PendingApproval, Approved, Retired }

// A recipe: how much of each material goes into one CFT of a product.
// An approved version is never edited. A change creates a new version.
public class MixDesign : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Empty means the general recipe for every plant that has no recipe of its own.
    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    // Counts up per product and plant, starting at 1. Assigned by the system.
    public int Version { get; set; }

    public MixDesignKind Kind { get; set; } = MixDesignKind.Standard;
    public MixDesignStatus Status { get; set; } = MixDesignStatus.Draft;
    public string? Notes { get; set; }

    // Why the approver sent it back. Cleared when it is submitted again.
    public string? RejectionReason { get; set; }

    // A design is effective from the moment it is approved.
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public List<MixDesignItem> Items { get; set; } = [];
}

public class MixDesignItem : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid MixDesignId { get; set; }
    public MixDesign MixDesign { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    // In the material's own unit (kg or litres) per CFT of concrete.
    public decimal QuantityPerCft { get; set; }
}