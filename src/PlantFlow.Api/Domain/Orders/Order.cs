using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Pricing;

namespace PlantFlow.Api.Domain.Orders;

public enum OrderStatus
{
    Draft, Submitted, InApproval, Approved, Confirmed, InProgress, Completed, OnHold, Rejected, Cancelled
}

public enum PourElement { Slab, Column, Footing, GroundLaying, Road, Other }

public enum PourMethod { Chute, Pump, Crane }

public class Order : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    // Counts up per company. Number is the same value formatted, for example SO-000042.
    public int Sequence { get; set; }
    public required string Number { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public Guid? SalesRepId { get; set; }
    public SalesRep? SalesRep { get; set; }

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public DateOnly RequestedDate { get; set; }

    // Plant to site, in km. Used for the delivery charge in the floor price.
    public decimal DistanceKm { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }

    // Counts up each time the order is submitted. Approvals belong to one round.
    public int ApprovalRound { get; set; }

    public List<OrderApproval> Approvals { get; set; } = [];

    public List<OrderLine> Lines { get; set; } = [];
    public List<OrderStatusHistory> History { get; set; } = [];
}

public class OrderLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // The approved recipe used for the last floor price calculation.
    public Guid? MixDesignId { get; set; }
    public MixDesign? MixDesign { get; set; }

    public decimal QuantityCft { get; set; }

    // Per CFT, excluding VAT. Copied from the active price unless the rep entered another one.
    public decimal UnitPrice { get; set; }

    // The active price that applied when the line was saved. A reference even if the rep changed the price.
    public Guid? PriceId { get; set; }
    public Price? Price { get; set; }

    // Per CFT, as calculated on the last save. Empty when it could not be calculated.
    public decimal? FloorPrice { get; set; }

    public int SlumpMm { get; set; }
    public PourElement PourElement { get; set; }
    public PourMethod PourMethod { get; set; }
    public string? Notes { get; set; }
}

public class OrderStatusHistory : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}