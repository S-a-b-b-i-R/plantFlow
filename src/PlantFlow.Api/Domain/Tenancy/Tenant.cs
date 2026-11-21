using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.Tenancy;

public enum TenantStatus { Active, Suspended }

// How new people get into a company. Only a join code today; single sign-on (a client's own identity
// provider, matched to the company) is meant to become a second value here, so people who joined by
// code keep working when a company moves to SSO.
public enum JoinMethod { Code }
public enum VolumeUnit { Cft, M3 }
public enum SurveyRequirement { Off, PumpJobs, All }

public class Tenant : AuditableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public JoinMethod JoinMethod { get; set; } = JoinMethod.Code;
    // Eight characters without look-alikes (see JoinCodes). Null until first shown, then always set.
    public string? JoinCode { get; set; }
    public DateTime? JoinCodeRenewedUtc { get; set; }

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
    public bool VatOnConcrete { get; set; } = true;
    // Used to work out when a pump and the first truck must leave the plant.
    public int AverageSpeedKmh { get; set; } = 25;
    public int PumpAssemblyMinutes { get; set; } = 90;

    // Used only for the estimate of truck loads in a plan.
    public decimal TruckLoadCft { get; set; } = 100m;

    // A pour step more than this many minutes behind plan is flagged late on the live board.
    public int LateThresholdMinutes { get; set; } = 15;

    // The plants' local time as minutes from UTC (Dhaka is +360). Shift and pour times are local.
    public int UtcOffsetMinutes { get; set; } = 360;

    // How long after pouring ends a part return can be recorded without a plant manager.
    public int ReturnWindowMinutes { get; set; } = 120;

    // A rep keeps a customer while they get an order confirmed at least this often. 0 = they keep it for good.
    public int CustomerOwnershipDays { get; set; } = 120;
}