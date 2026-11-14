using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.Tenancy;

public enum TenantStatus { Active, Suspended }
public enum VolumeUnit { Cft, M3 }
public enum SurveyRequirement { Off, PumpJobs, All }

public class Tenant : AuditableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    // Display unit only. Volumes are always stored in CFT.
    public VolumeUnit VolumeUnit { get; set; } = VolumeUnit.Cft;

    // Pump is free when the order's pump quantity reaches this (CFT). Null = never free.
    public decimal? PumpFreeAboveCft { get; set; }
    public SurveyRequirement RequireSurveyBeforePlanning { get; set; } = SurveyRequirement.PumpJobs;
    // public decimal MinMarginPercent { get; set; }
    // Pumps rest for this many following shifts of the same day after use. 0 turns the rule off.
    public int PumpRestShifts { get; set; } = 1;
    // When on, every order also needs a Credit approval.
    public bool RequireCreditApproval { get; set; }
}