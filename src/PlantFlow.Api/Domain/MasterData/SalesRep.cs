using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

// A salesperson who owns customers and, later, targets. Not the same thing as a login user:
// the link is optional, so reps can exist before (or without) having an account.
public class SalesRep : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }
    public required string Name { get; set; }
    public string? Region { get; set; }

    // Who this rep reports to (a sales manager, who is also a SalesRep record).
    public Guid? ManagerId { get; set; }
    public ActiveStatus Status { get; set; } = ActiveStatus.Active;
}