using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.Orders;

// How the billed quantity of a line was decided.
public enum BillingBasis { Delivered, Ordered, Custom }

public enum AdjustmentKind { Return, QcReject, Discount, Other }

// Reduces what the customer pays, for example a rejected truck. The amount excludes VAT.
public class OrderAdjustment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public AdjustmentKind Kind { get; set; }
    public string Reason { get; set; } = "";
    public decimal? QuantityCft { get; set; }
    public decimal Amount { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}