using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Domain.Auditing;
using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Domain.Tenancy;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.MixDesigns;
using PlantFlow.Api.Domain.Costing;
using PlantFlow.Api.Domain.Pricing;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Data;

public class PlantFlowDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public PlantFlowDbContext(DbContextOptions<PlantFlowDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // Read by the global query filters. Guid.Empty matches no rows,
    // so with no tenant resolved a query returns no tenant data.
    public Guid CurrentTenantId => _currentUser.TenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<BatchingPlant> BatchingPlants => Set<BatchingPlant>();
    public DbSet<SalesRep> SalesReps => Set<SalesRep>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ServiceCharge> ServiceCharges => Set<ServiceCharge>();
    public DbSet<PlantShift> PlantShifts => Set<PlantShift>();
    public DbSet<MixDesign> MixDesigns => Set<MixDesign>();
    public DbSet<MixDesignItem> MixDesignItems => Set<MixDesignItem>();
    public DbSet<MaterialCost> MaterialCosts => Set<MaterialCost>();
    public DbSet<OverheadCost> OverheadCosts => Set<OverheadCost>();
    public DbSet<DeliveryChargeBand> DeliveryChargeBands => Set<DeliveryChargeBand>();
    public DbSet<PricingPolicy> PricingPolicies => Set<PricingPolicy>();
    public DbSet<AitRate> AitRates => Set<AitRate>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<SitePlantDistance> SitePlantDistances => Set<SitePlantDistance>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderApproval> OrderApprovals => Set<OrderApproval>();
    public DbSet<OrderCharge> OrderCharges => Set<OrderCharge>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlantFlowDbContext).Assembly);

        // Every entity that implements ITenantOwned gets the tenant filter automatically,
        // so a new entity can't be added without it.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)) continue;

            typeof(PlantFlowDbContext)
                .GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTenantAndAuditFields()
    {
        var now = DateTime.UtcNow;
        var userId = _currentUser.UserId;
        var tenantId = _currentUser.TenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added
                && entry.Entity is ITenantOwned owned
                && owned.TenantId == Guid.Empty
                && tenantId is not null)
            {
                owned.TenantId = tenantId.Value;
            }

            if (entry.Entity is not AuditableEntity auditable) continue;

            if (entry.State == EntityState.Added)
            {
                auditable.CreatedAtUtc = now;
                auditable.CreatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                auditable.UpdatedAtUtc = now;
                auditable.UpdatedBy = userId;
            }
        }
    }
}