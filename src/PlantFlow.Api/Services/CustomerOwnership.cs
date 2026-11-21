using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;

namespace PlantFlow.Api.Services;

// One sales rep owns each customer across the company. The owner keeps it while they get an order
// confirmed at least every N days (Tenant.CustomerOwnershipDays, 0 = never runs out). After that, or once
// a manager releases it, the customer is open: any rep can sell to it, and the first confirmed order wins it.
// While a customer is owned, other reps don't see it, except to finish their own orders already under way.
public class CustomerOwnership(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit)
{
    // Restricted = a rep who only sees their own and open customers. Managers, Admin and everyone
    // without customers.manage (planners, accounts) see every customer.
    public sealed record Scope(bool Restricted, Guid? RepId, int Days, DateTime NowUtc)
    {
        // An owner whose clock started on or before this has run out of days.
        public DateTime LapsedFrom => NowUtc.AddDays(-Days);
    }

    public static bool IsRep(ClaimsPrincipal user) =>
        user.HasClaim(PlantFlowClaims.Permission, Permissions.CustomersManage)
        && !user.HasClaim(PlantFlowClaims.Permission, Permissions.CustomersAssign);

    public async Task<Scope> ScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var days = await DaysAsync(cancellationToken);
        return new Scope(IsRep(user), await MyRepIdAsync(cancellationToken), days, DateTime.UtcNow);
    }

    public Task<int> DaysAsync(CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking().Where(t => t.Id == currentUser.TenantId)
            .Select(t => t.CustomerOwnershipDays).FirstAsync(cancellationToken);

    // The signed-in user's active sales rep, if their login is linked to one.
    public Task<Guid?> MyRepIdAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return db.SalesReps.Where(r => r.UserId == userId && r.Status == ActiveStatus.Active)
            .Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public static IQueryable<Customer> Open(IQueryable<Customer> query, Scope scope)
    {
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        return query.Where(c => c.SalesRepId == null || (days > 0 && c.OwnershipStartedUtc <= lapsedFrom));
    }

    // Owned by this rep and still within its days.
    public static IQueryable<Customer> Mine(IQueryable<Customer> query, Scope scope)
    {
        var rep = scope.RepId;
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        return query.Where(c => rep != null && c.SalesRepId == rep && !(days > 0 && c.OwnershipStartedUtc <= lapsedFrom));
    }

    // Customers this user may start orders for or edit: their own and the open ones.
    public static IQueryable<Customer> Workable(IQueryable<Customer> query, Scope scope)
    {
        if (!scope.Restricted) return query;
        var rep = scope.RepId;
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        return query.Where(c => c.SalesRepId == null || (days > 0 && c.OwnershipStartedUtc <= lapsedFrom)
                                || (rep != null && c.SalesRepId == rep));
    }

    // Customers this user may see: workable ones, plus any where they still have an order under way.
    public IQueryable<Customer> Visible(IQueryable<Customer> query, Scope scope)
    {
        if (!scope.Restricted) return query;
        var rep = scope.RepId;
        var days = scope.Days;
        var lapsedFrom = scope.LapsedFrom;
        var closed = PlantUsage.ClosedStatuses;
        return query.Where(c => c.SalesRepId == null || (days > 0 && c.OwnershipStartedUtc <= lapsedFrom)
                                || (rep != null && c.SalesRepId == rep)
                                || (rep != null && db.Orders.Any(o =>
                                    o.CustomerId == c.Id && o.SalesRepId == rep && !closed.Contains(o.Status))));
    }

    public Task<bool> CanSeeAsync(Guid customerId, Scope scope, CancellationToken cancellationToken) =>
        Visible(db.Customers, scope).AnyAsync(c => c.Id == customerId, cancellationToken);

    public Task<bool> CanWorkAsync(Guid customerId, Scope scope, CancellationToken cancellationToken) =>
        Workable(db.Customers, scope).AnyAsync(c => c.Id == customerId, cancellationToken);

    // How many active customers opened up after the given time (all open ones when it is null).
    // A released customer opened when it was released; a lapsed one when its days ran out.
    public static Task<int> OpenedSinceAsync(
        IQueryable<Customer> customers, Scope scope, DateTime? seenUtc, CancellationToken cancellationToken)
    {
        var open = Open(customers.Where(c => c.Status == ActiveStatus.Active), scope);
        if (seenUtc is { } seen)
        {
            var lapsedAfter = seen.AddDays(-scope.Days);
            open = open.Where(c => c.SalesRepId == null ? c.OwnershipStartedUtc > seen : c.OwnershipStartedUtc > lapsedAfter);
        }

        return open.CountAsync(cancellationToken);
    }

    // Called when an order is confirmed. The owner's clock restarts; an open customer goes to the order's rep.
    // A rep finishing an order for a customer someone else now owns changes nothing.
    public async Task OnConfirmedAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.SalesRepId is not { } repId) return;

        var customer = await db.Customers.FirstAsync(c => c.Id == order.CustomerId, cancellationToken);
        var now = DateTime.UtcNow;

        if (customer.SalesRepId == repId)
        {
            customer.OwnershipStartedUtc = now;
            return;
        }

        var days = await DaysAsync(cancellationToken);
        var isOpen = customer.SalesRepId is null || (days > 0 && customer.OwnershipStartedUtc <= now.AddDays(-days));
        if (!isOpen) return;

        var previous = customer.SalesRepId;
        customer.SalesRepId = repId;
        customer.OwnershipStartedUtc = now;
        audit.Record(customer.TenantId, "customer.owner_won", "Customer", customer.Id.ToString(),
            new { customer.Code, PreviousSalesRepId = previous, SalesRepId = repId, Order = order.Number });
    }
}
