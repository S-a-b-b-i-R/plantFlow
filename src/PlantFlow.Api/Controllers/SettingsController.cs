using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record TenantSettingsDto(
    string Name, string Slug, VolumeUnit VolumeUnit, decimal? PumpFreeAboveCft,
    SurveyRequirement RequireSurveyBeforePlanning, int PumpRestShifts,
    bool RequireCreditApproval, bool VatOnConcrete, int AverageSpeedKmh, int PumpAssemblyMinutes, decimal TruckLoadCft);

// What every signed-in user needs to display things. Nothing commercial.
public record PublicSettingsDto(string Name, VolumeUnit VolumeUnit);

public class TenantSettingsRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; init; } = "";

    // Display only. Volumes are always stored in CFT.
    [Required]
    public VolumeUnit? VolumeUnit { get; init; }

    // A pump is free when the order's pump volume reaches this many CFT. Empty means never free.
    [Range(0.01, 10_000_000.0)]
    public decimal? PumpFreeAboveCft { get; init; }

    [Required]
    public SurveyRequirement? RequireSurveyBeforePlanning { get; init; }

    // Pumps rest for this many following shifts of the same day. 0 turns the rule off.
    [Range(0, 3)]
    public int PumpRestShifts { get; init; } = 1;

    // When on, every order also needs a Credit approval.
    public bool RequireCreditApproval { get; init; }

    // Whether the concrete itself carries VAT. Leave it out to keep it on.
    public bool VatOnConcrete { get; init; } = true;

    // Pump and first-truck leave times use this speed. Leave it out to keep 25.
    [Range(1, 120)]
    public int AverageSpeedKmh { get; init; } = 25;

    // Default minutes to assemble a pump on site.
    [Range(0, 480)]
    public int PumpAssemblyMinutes { get; init; } = 90;

    // Used for the estimate of truck loads in a plan.
    [Range(1.0, 100000.0)]
    public decimal TruckLoadCft { get; init; } = 100m;
}

[ApiController]
[Route("api/settings")]
public class SettingsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private static TenantSettingsDto ToDto(Tenant t) => new(
        t.Name, t.Slug, t.VolumeUnit, t.PumpFreeAboveCft, t.RequireSurveyBeforePlanning, t.PumpRestShifts, t.RequireCreditApproval, t.VatOnConcrete, t.AverageSpeedKmh, t.PumpAssemblyMinutes, t.TruckLoadCft);

    [HttpGet]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<TenantSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var tenant = await LoadTenantAsync(cancellationToken);
        return ToDto(tenant);
    }

    // No permission attribute: the default policy applies, so any active signed-in user can read it.
    [HttpGet("public")]
    public async Task<ActionResult<PublicSettingsDto>> GetPublic(CancellationToken cancellationToken)
    {
        var tenant = await LoadTenantAsync(cancellationToken);
        return new PublicSettingsDto(tenant.Name, tenant.VolumeUnit);
    }

    [HttpPut]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Update(TenantSettingsRequest request, CancellationToken cancellationToken)
    {
        // Money and volume thresholds have two decimals. Refuse extra digits instead of rounding them away.
        if (request.PumpFreeAboveCft is { } threshold && threshold != Math.Round(threshold, 2))
        {
            return Problem(title: "The pump threshold can have at most 2 decimal places.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenant = await LoadTenantAsync(cancellationToken);
        var before = ToDto(tenant);

        tenant.Name = request.Name.Trim();
        tenant.VolumeUnit = request.VolumeUnit!.Value;
        tenant.PumpFreeAboveCft = request.PumpFreeAboveCft;
        tenant.RequireSurveyBeforePlanning = request.RequireSurveyBeforePlanning!.Value;
        tenant.PumpRestShifts = request.PumpRestShifts;
        tenant.RequireCreditApproval = request.RequireCreditApproval;
        tenant.VatOnConcrete = request.VatOnConcrete;
        tenant.AverageSpeedKmh = request.AverageSpeedKmh;
        tenant.PumpAssemblyMinutes = request.PumpAssemblyMinutes;
        tenant.TruckLoadCft = request.TruckLoadCft;

        audit.Record(tenant.Id, "tenant.settings_changed", "Tenant", tenant.Id.ToString(),
            new
            {
                Before = before,
                After = ToDto(tenant),
                VatOnConcreteChanged = before.VatOnConcrete != tenant.VatOnConcrete,
                PumpAssemblyMinutesChanged = before.PumpAssemblyMinutes != tenant.PumpAssemblyMinutes,
                TruckLoadCftChanged = before.TruckLoadCft != tenant.TruckLoadCft
            });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Tenants are not tenant-filtered (they are the tenants), so pick the signed-in user's own explicitly.
    private Task<Tenant> LoadTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        return db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);
    }
}