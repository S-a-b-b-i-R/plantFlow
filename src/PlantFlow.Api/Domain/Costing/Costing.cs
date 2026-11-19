using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Domain.Costing;

public enum OverheadKind { Overhead, Fuel, BridgeToll }

public enum CostStatus { Draft, PendingApproval, Approved, Retired }

// Material costs, overheads and delivery bands only count in a floor price once management has approved them.
// An approved row is never edited. A change is a new row; the old one is retired.
public abstract class ApprovableCost : AuditableEntity
{
    public CostStatus Status { get; set; } = CostStatus.Draft;

    // Why the approver sent it back. Cleared when it is submitted again.
    public string? RejectionReason { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

// What one unit of a material costs, in the material's own unit (kg, litre or tonne).
// A history: the latest row on or before a date applies. A plant row beats the general row.
public class MaterialCost : ApprovableCost, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public decimal CostPerUnit { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

// A cost per CFT that is not a material: overhead, fuel or bridge toll.
public class OverheadCost : ApprovableCost, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public OverheadKind Kind { get; set; }
    public required string Name { get; set; }

    // Empty means every plant. A bridge toll always names its plant.
    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public decimal AmountPerCft { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

// Delivery charge per CFT when the site is more than FromKm and up to ToKm from the plant.
public class DeliveryChargeBand : ApprovableCost, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public decimal FromKm { get; set; }
    public decimal? ToKm { get; set; }
    public decimal ChargePerCft { get; set; }
}

// One row per company. Created the first time it is saved. Until then the defaults apply.
public class PricingPolicy : AuditableEntity, ITenantOwned
{
    public const decimal DefaultVatPercent = 15m;
    public const decimal DefaultMinMarginPercent = 10m;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public decimal VatPercent { get; set; } = DefaultVatPercent;

    // Profit as a percent of the selling price.
    public decimal MinMarginPercent { get; set; } = DefaultMinMarginPercent;

    public List<AitRate> AitRates { get; set; } = [];
}

// Advance income tax the customer deducts from payment, by customer type.
public class AitRate : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PricingPolicyId { get; set; }
    public PricingPolicy PricingPolicy { get; set; } = null!;

    public CustomerType CustomerType { get; set; }
    public decimal Percent { get; set; }
}