using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// Someone who assembles and runs a pump on site. Operators are not drivers: drivers go with transit mixers.
// An operator is in at most one pump's crew. Phone is required for new and edited operators; operators
// carried over from the old crew names may still lack one.
public class PumpOperator : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Name { get; set; }
    public string? Phone { get; set; }

    public Guid? PumpId { get; set; }
    public Vehicle? Pump { get; set; }

    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}
