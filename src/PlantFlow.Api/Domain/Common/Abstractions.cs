namespace PlantFlow.Api.Domain.Common;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}