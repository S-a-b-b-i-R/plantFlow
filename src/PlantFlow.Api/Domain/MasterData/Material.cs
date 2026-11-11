using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

public enum MaterialCategory { Cement, Sand, Stone, Water, Admixture, Additive }

public enum MaterialUnit { Kg, Litre, Tonne }

// A raw ingredient of a concrete mix. Mix designs will say how much of each goes into one CFT.
public class Material : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
    public MaterialCategory Category { get; set; }
    public MaterialUnit Unit { get; set; } = MaterialUnit.Kg;
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}