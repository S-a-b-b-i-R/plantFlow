using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Domain.Orders;

// A charge on top of the concrete, with its rate frozen when the order was confirmed.
public class OrderCharge : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public ServiceChargeKind Kind { get; set; }
    public Guid? ServiceChargeId { get; set; }

    public string Description { get; set; } = "";
    public decimal QuantityCft { get; set; }
    public decimal RatePerCft { get; set; }
    public decimal Amount { get; set; }
}