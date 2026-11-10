using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.Auditing;

public class AuditLog : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? ChangesJson { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}