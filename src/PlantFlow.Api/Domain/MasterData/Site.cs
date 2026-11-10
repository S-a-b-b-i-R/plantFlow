using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// A project location where concrete is poured. A customer has many; site surveys will belong to a site.
public class Site : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public required string Name { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? AccessNotes { get; set; }
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}