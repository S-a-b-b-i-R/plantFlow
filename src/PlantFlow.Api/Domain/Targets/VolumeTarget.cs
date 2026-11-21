using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;

namespace PlantFlow.Api.Domain.Targets;

// Company: one per quarter. Plant: one per plant. Rep: one per sales rep, across every plant they sell from.
public enum TargetLevel { Company, Plant, Rep }

// The volume a company, plant or sales rep aims to bill in one quarter of the company's year. Year is the
// calendar year the company's year starts in, so with a July start "2026 Q1" runs July to September 2026.
// Achievement is never stored: it is worked out from billed orders.
public class VolumeTarget : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public int Year { get; set; }
    public int Quarter { get; set; }
    public TargetLevel Level { get; set; }

    // Set for a plant target.
    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }

    // Set for a rep target.
    public Guid? SalesRepId { get; set; }
    public SalesRep? SalesRep { get; set; }

    public decimal VolumeCft { get; set; }
}
