using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// A sellable concrete grade, for example "M25, 20 mm aggregate, 100 mm slump".
public class Product : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }

    // Free text because companies name grades differently (M25, C30, 3000 psi).
    public required string StrengthClass { get; set; }

    public int MaxAggregateMm { get; set; }
    public int DefaultSlumpMm { get; set; }
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}