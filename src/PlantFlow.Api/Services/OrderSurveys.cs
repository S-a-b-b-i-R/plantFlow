using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Tenancy;

namespace PlantFlow.Api.Services;

// The survey an order relies on: one of its site's last three surveys, or a request for a new one.
// Every method that refuses returns a plain-language reason; null means the step is allowed.
public class OrderSurveys(PlantFlowDbContext db)
{
    public const int TaggableCount = 3;

    // Orders in these states can still change which survey they rely on (until a pour is planned).
    public static readonly OrderStatus[] OpenStatuses =
    [
        OrderStatus.Draft, OrderStatus.Submitted, OrderStatus.InApproval,
        OrderStatus.Approved, OrderStatus.Confirmed, OrderStatus.OnHold
    ];

    public const string Missing =
        "This order needs a site survey. Tag one of the site's last three surveys or request a new one.";

    public const string Locked = "The survey cannot be changed after a pour that relies on it is planned.";

    public static bool Needs(SurveyRequirement rule, IEnumerable<PourMethod> methods) =>
        rule == SurveyRequirement.All
        || (rule == SurveyRequirement.PumpJobs && methods.Any(m => m == PourMethod.Pump));

    // Null when the order has what the company's survey rule asks for.
    public async Task<string?> RequirementProblemAsync(
        Order order, IEnumerable<PourMethod> methods, CancellationToken cancellationToken)
    {
        if (order.SiteSurveyId is not null || order.SurveyRequested) return null;

        var rule = await db.Tenants.AsNoTracking().Where(t => t.Id == order.TenantId)
            .Select(t => t.RequireSurveyBeforePlanning).FirstAsync(cancellationToken);
        return Needs(rule, methods) ? Missing : null;
    }

    // Checks a survey choice for an order at a site. The existing order is null on create.
    public async Task<string?> ChoiceProblemAsync(
        Order? existing, Guid siteId, Guid? surveyId, bool requestNew, CancellationToken cancellationToken)
    {
        if (surveyId is not null && requestNew)
        {
            return "Either tag a survey or request a new one, not both.";
        }

        var changed = existing is null
                      || existing.SiteSurveyId != surveyId
                      || existing.SurveyRequested != requestNew;
        if (!changed && existing!.SiteId == siteId) return null;

        if (existing is not null && changed && await HasPlannedPourAsync(existing.Id, cancellationToken))
        {
            return Locked;
        }

        if (surveyId is { } id && (changed || existing!.SiteId != siteId))
        {
            var lastThree = await db.SiteSurveys.AsNoTracking().Where(s => s.SiteId == siteId)
                .OrderByDescending(s => s.SurveyedOn).ThenByDescending(s => s.CreatedAtUtc)
                .Take(TaggableCount).Select(s => s.Id).ToListAsync(cancellationToken);
            if (!lastThree.Contains(id))
            {
                return "Tag one of this site's last three surveys.";
            }
        }

        return null;
    }

    // Applies an accepted choice. A request keeps its original time and requester while it stays open.
    public static void Apply(Order order, Guid? surveyId, bool requestNew, Guid? userId)
    {
        order.SiteSurveyId = surveyId;
        if (requestNew && !order.SurveyRequested)
        {
            order.SurveyRequested = true;
            order.SurveyRequestedAtUtc = DateTime.UtcNow;
            order.SurveyRequestedBy = userId;
        }
        else if (!requestNew)
        {
            order.SurveyRequested = false;
            order.SurveyRequestedAtUtc = null;
            order.SurveyRequestedBy = null;
        }
    }

    // Locks the choice only once a pour that relied on the survey is planned: any pour under the All rule,
    // a pumped pour under PumpJobs, none when surveys are off. A planned chute pour does not lock it.
    public async Task<bool> HasPlannedPourAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = await db.Orders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => o.TenantId).FirstAsync(cancellationToken);
        var rule = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => t.RequireSurveyBeforePlanning).FirstAsync(cancellationToken);

        return rule switch
        {
            SurveyRequirement.All => await db.Pours.AnyAsync(p => p.OrderLine.OrderId == orderId, cancellationToken),
            SurveyRequirement.PumpJobs => await db.Pours.AnyAsync(
                p => p.OrderLine.OrderId == orderId && p.OrderLine.PourMethod == PourMethod.Pump, cancellationToken),
            _ => false
        };
    }
}
