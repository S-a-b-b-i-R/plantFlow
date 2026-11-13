using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// How far a site is from a plant, in km. Orders copy it as a suggestion.
public class SitePlantDistance : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public decimal DistanceKm { get; set; }
}