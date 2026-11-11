using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

public class Driver : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Name { get; set; }
    public string? Phone { get; set; }
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}