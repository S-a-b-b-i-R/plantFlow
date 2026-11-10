using System.Text.Json;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Auditing;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Services;

// Adds an audit entry to the current unit of work. It is saved together with the change it describes.
public class AuditWriter(PlantFlowDbContext db, ICurrentUser currentUser)
{
    public void Record(Guid tenantId, string action, string entityType, string entityId, object? changes = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = currentUser.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChangesJson = changes is null ? null : JsonSerializer.Serialize(changes)
        });
    }
}