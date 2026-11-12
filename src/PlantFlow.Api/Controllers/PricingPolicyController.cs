using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record AitRateDto(CustomerType CustomerType, decimal Percent);

public record PricingPolicyDto(decimal VatPercent, decimal MinMarginPercent, List<AitRateDto> AitRates);

public class AitRateRequest
{
    [Required]
    public CustomerType? CustomerType { get; init; }

    [Range(0.0, 30.0)]
    public decimal Percent { get; init; }
}

public class UpdatePricingPolicyRequest
{
    [Range(0.0, 100.0)]
    public decimal VatPercent { get; init; }

    // Profit as a percent of the selling price.
    [Range(0.0, 60.0)]
    public decimal MinMarginPercent { get; init; }

    // A customer type that is not listed pays no AIT. The list replaces the old one.
    [Required]
    public List<AitRateRequest> AitRates { get; init; } = [];
}

[ApiController]
[Route("api/pricing-policy")]
public class PricingPolicyController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<PricingPolicyDto>> Get(CancellationToken cancellationToken)
    {
        var policy = await db.PricingPolicies.AsNoTracking().Include(p => p.AitRates).FirstOrDefaultAsync(cancellationToken);

        if (policy is null)
        {
            return new PricingPolicyDto(PricingPolicy.DefaultVatPercent, PricingPolicy.DefaultMinMarginPercent, []);
        }

        return new PricingPolicyDto(
            policy.VatPercent, policy.MinMarginPercent,
            policy.AitRates.OrderBy(r => r.CustomerType).Select(r => new AitRateDto(r.CustomerType, r.Percent)).ToList());
    }

    [HttpPut]
    [HasPermission(Permissions.CostsManage)]
    public async Task<IActionResult> Update(UpdatePricingPolicyRequest request, CancellationToken cancellationToken)
    {
        var percents = request.AitRates.Select(r => r.Percent).Append(request.VatPercent).Append(request.MinMarginPercent);
        if (percents.Any(p => p != Math.Round(p, 2)))
        {
            return Problem(title: "Percentages can have at most 2 decimal places.", statusCode: StatusCodes.Status400BadRequest);
        }

        var types = request.AitRates.Select(r => r.CustomerType!.Value).ToList();
        if (types.Distinct().Count() != types.Count)
        {
            return Problem(title: "Each customer type can appear only once.", statusCode: StatusCodes.Status400BadRequest);
        }

        var policy = await db.PricingPolicies.Include(p => p.AitRates).FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = new PricingPolicy { TenantId = currentUser.TenantId!.Value };
            db.PricingPolicies.Add(policy);
        }

        policy.VatPercent = request.VatPercent;
        policy.MinMarginPercent = request.MinMarginPercent;

        // Update in place, add new ones explicitly, remove the ones no longer listed.
        var existing = policy.AitRates.ToDictionary(r => r.CustomerType);
        foreach (var wanted in request.AitRates)
        {
            var type = wanted.CustomerType!.Value;
            if (existing.TryGetValue(type, out var current))
            {
                current.Percent = wanted.Percent;
            }
            else
            {
                db.AitRates.Add(new AitRate
                {
                    TenantId = policy.TenantId,
                    PricingPolicyId = policy.Id,
                    CustomerType = type,
                    Percent = wanted.Percent
                });
            }
        }

        db.AitRates.RemoveRange(policy.AitRates.Where(r => !types.Contains(r.CustomerType)));

        audit.Record(policy.TenantId, "pricingpolicy.updated", "PricingPolicy", policy.Id.ToString(),
            new { policy.VatPercent, policy.MinMarginPercent, AitRates = request.AitRates.Select(r => new { r.CustomerType, r.Percent }) });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}