using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// The fees the system understands. A company has at most one charge of each kind.
public enum ServiceChargeKind { Pump, NightDelivery, ShortLoad, ExtraDistance }

public enum ChargeBasis { PerCft, PerLoad, Fixed }

// An extra fee that can be added to an order. The order can override the default amount.
public class ServiceCharge : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
    public ServiceChargeKind Kind { get; set; }
    public ChargeBasis Basis { get; set; }
    public decimal DefaultAmount { get; set; }
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
    public bool VatApplies { get; set; } = true;
}