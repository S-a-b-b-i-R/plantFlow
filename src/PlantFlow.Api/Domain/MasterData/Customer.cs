using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

public enum CustomerType { Contractor, Developer, Individual }

public class Customer : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
    public CustomerType Type { get; set; } = CustomerType.Contractor;
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public Guid? SalesRepId { get; set; }
    public SalesRep? SalesRep { get; set; }

    // When the current owner's clock started: assignment or their last confirmed order. With no owner,
    // when the customer was released (or created without one). See CustomerOwnership.
    public DateTime OwnershipStartedUtc { get; set; } = DateTime.UtcNow;

    public ActiveStatus Status { get; set; } = ActiveStatus.Active;

    public List<Site> Sites { get; set; } = [];
    public bool VatExempt { get; set; }
}