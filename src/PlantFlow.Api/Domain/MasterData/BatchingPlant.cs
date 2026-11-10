using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// The machine that mixes concrete. A plant can have several, each with its own make and capacity.
public class BatchingPlant : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public required string Code { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public decimal RatedCapacityCftPerHour { get; set; }
    public int BatchDurationMinutes { get; set; } = 8;
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}