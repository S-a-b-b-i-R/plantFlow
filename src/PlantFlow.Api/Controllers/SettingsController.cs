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
    bool RequireCreditApproval, bool VatOnConcrete, int AverageSpeedKmh, int PumpAssemblyMinutes, decimal TruckLoadCft, int LateThresholdMinutes, int UtcOffsetMinutes, int ReturnWindowMinutes, int CustomerOwnershipDays,
    int YearStartMonth,
    // True once the company has any order. The time zone decides which day a shift falls on, so it is fixed from then on.
    bool TimeZoneLocked = false,
    // True once any target is set. The year's start month decides which months each quarter covers.
    bool YearStartLocked = false);

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

    // Leave out to keep the current value.
    [Range(0, 1440)]
    public int? LateThresholdMinutes { get; init; }

    [Range(-720, 840)]
    public int? UtcOffsetMinutes { get; init; }

    [Range(0, 1440)]
    public int? ReturnWindowMinutes { get; init; }

    // A rep keeps a customer while they get an order confirmed at least this often. 0 = for good.
    // Leave out to keep the current value.
    [Range(0, 3650)]
    public int? CustomerOwnershipDays { get; init; }

    // The month (1-12) the company's year starts in, for quarterly targets. Leave out to keep the current value.
    [Range(1, 12)]
    public int? YearStartMonth { get; init; }

    // Needed when VAT on concrete, credit approval or the free pump threshold changes. Kept in the audit log.
    [StringLength(500)]
    public string? Reason { get; init; }
}

[ApiController]
[Route("api/settings")]
public class SettingsController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    public const string TimeZoneLockedMessage =
        "The time zone can't change once the company has orders, because it decides which day each shift and pour falls on.";

    public const string YearStartLockedMessage =
        "The month the year starts can't change once targets are set, because it decides which months each quarter covers.";

    private static TenantSettingsDto ToDto(Tenant t, bool timeZoneLocked = false, bool yearStartLocked = false) => new(
        t.Name, t.Slug, t.VolumeUnit, t.PumpFreeAboveCft, t.RequireSurveyBeforePlanning, t.PumpRestShifts, t.RequireCreditApproval, t.VatOnConcrete, t.AverageSpeedKmh, t.PumpAssemblyMinutes, t.TruckLoadCft, t.LateThresholdMinutes, t.UtcOffsetMinutes, t.ReturnWindowMinutes, t.CustomerOwnershipDays, t.YearStartMonth, timeZoneLocked, yearStartLocked);

    [HttpGet]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<ActionResult<TenantSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var tenant = await LoadTenantAsync(cancellationToken);
        return ToDto(tenant, await db.Orders.AnyAsync(cancellationToken), await db.VolumeTargets.AnyAsync(cancellationToken));
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

        if (request.UtcOffsetMinutes is { } newOffset && newOffset != tenant.UtcOffsetMinutes
            && await db.Orders.AnyAsync(cancellationToken))
        {
            return Problem(title: TimeZoneLockedMessage, statusCode: StatusCodes.Status409Conflict);
        }

        if (request.YearStartMonth is { } newStart && newStart != tenant.YearStartMonth
            && await db.VolumeTargets.AnyAsync(cancellationToken))
        {
            return Problem(title: YearStartLockedMessage, statusCode: StatusCodes.Status409Conflict);
        }

        // These change what customers pay or who must approve an order, so the change carries a reason.
        var needsReason = new List<string>();
        if (request.VatOnConcrete != tenant.VatOnConcrete) needsReason.Add("VAT on concrete");
        if (request.RequireCreditApproval != tenant.RequireCreditApproval) needsReason.Add("credit approval");
        if (request.PumpFreeAboveCft != tenant.PumpFreeAboveCft) needsReason.Add("the free pump threshold");
        var reason = request.Reason?.Trim();
        if (needsReason.Count > 0 && string.IsNullOrEmpty(reason))
        {
            return Problem(title: ReasonMessage(needsReason), statusCode: StatusCodes.Status400BadRequest);
        }

        tenant.Name = request.Name.Trim();
        tenant.VolumeUnit = request.VolumeUnit!.Value;
        tenant.PumpFreeAboveCft = request.PumpFreeAboveCft;
        tenant.RequireSurveyBeforePlanning = request.RequireSurveyBeforePlanning!.Value;
        tenant.PumpRestShifts = request.PumpRestShifts;
        tenant.RequireCreditApproval = request.RequireCreditApproval;
        tenant.VatOnConcrete = request.VatOnConcrete;
        tenant.AverageSpeedKmh = request.AverageSpeedKmh;
        tenant.PumpAssemblyMinutes = request.PumpAssemblyMinutes;
        if (request.LateThresholdMinutes is { } late) tenant.LateThresholdMinutes = late;
        if (request.UtcOffsetMinutes is { } offset) tenant.UtcOffsetMinutes = offset;
        if (request.ReturnWindowMinutes is { } returnWindow) tenant.ReturnWindowMinutes = returnWindow;
        if (request.CustomerOwnershipDays is { } ownershipDays) tenant.CustomerOwnershipDays = ownershipDays;
        if (request.YearStartMonth is { } yearStart) tenant.YearStartMonth = yearStart;
        tenant.TruckLoadCft = request.TruckLoadCft;

        audit.Record(tenant.Id, "tenant.settings_changed", "Tenant", tenant.Id.ToString(),
            new
            {
                Before = before,
                After = ToDto(tenant),
                VatOnConcreteChanged = before.VatOnConcrete != tenant.VatOnConcrete,
                PumpAssemblyMinutesChanged = before.PumpAssemblyMinutes != tenant.PumpAssemblyMinutes,
                TruckLoadCftChanged = before.TruckLoadCft != tenant.TruckLoadCft,
                LateThresholdMinutesChanged = before.LateThresholdMinutes != tenant.LateThresholdMinutes,
                UtcOffsetMinutesChanged = before.UtcOffsetMinutes != tenant.UtcOffsetMinutes,
                ReturnWindowMinutesChanged = before.ReturnWindowMinutes != tenant.ReturnWindowMinutes,
                CustomerOwnershipDaysChanged = before.CustomerOwnershipDays != tenant.CustomerOwnershipDays,
                YearStartMonthChanged = before.YearStartMonth != tenant.YearStartMonth,
                Reason = string.IsNullOrEmpty(reason) ? null : reason
            });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // "Give a reason for changing VAT on concrete and credit approval."
    public static string ReasonMessage(IReadOnlyList<string> changed)
    {
        var list = changed.Count == 1
            ? changed[0]
            : string.Join(", ", changed.Take(changed.Count - 1)) + " and " + changed[^1];
        return $"Give a reason for changing {list}.";
    }

    // Tenants are not tenant-filtered (they are the tenants), so pick the signed-in user's own explicitly.
    private Task<Tenant> LoadTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        return db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);
    }
}