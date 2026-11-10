using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

public class Plant : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }

    // IANA name such as "Asia/Dhaka". Timestamps are stored in UTC and shown in this zone.
    public string TimeZone { get; set; } = "UTC";
    public decimal OperatingHoursPerDay { get; set; } = 10;
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;

    public List<BatchingPlant> BatchingPlants { get; set; } = [];
}