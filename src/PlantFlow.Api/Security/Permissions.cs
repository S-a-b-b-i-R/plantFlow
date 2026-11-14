using System.Reflection;

namespace PlantFlow.Api.Domain.Security;

public static class Permissions
{
    // Administration
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";

    // Master data, mix designs, costs and prices
    public const string MasterDataView = "masterdata.view";
    public const string MasterDataEdit = "masterdata.edit";
    public const string MixDesignsView = "mixdesigns.view";
    public const string MixDesignsEdit = "mixdesigns.edit";
    public const string MixDesignsApprove = "mixdesigns.approve";
    public const string CostsManage = "costs.manage";
    public const string PricesView = "prices.view";
    public const string PricesEdit = "prices.edit";
    public const string PricesApprove = "prices.approve";

    // Orders and billing
    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersEdit = "orders.edit";
    public const string OrdersApproveSalesManager = "orders.approve.salesmanager";
    public const string OrdersApprovePricing = "orders.approve.pricing";
    public const string OrdersApproveManagement = "orders.approve.management";
    public const string OrdersApproveTechnical = "orders.approve.technical";
    public const string OrdersApproveCredit = "orders.approve.credit";
    public const string OrdersConfirm = "orders.confirm";
    public const string OrdersClose = "orders.close";
    public const string OrdersBillingResolve = "orders.billing.resolve";
    public const string LedgerView = "ledger.view";
    public const string LedgerManage = "ledger.manage";

    // Site surveys
    public const string SiteSurveysView = "sitesurveys.view";
    public const string SiteSurveysPerform = "sitesurveys.perform";
    public const string SiteSurveysAttach = "sitesurveys.attach";

    // Delivery, batching and returns
    public const string DeliveriesPlan = "deliveries.plan";
    public const string DeliveriesDispatch = "deliveries.dispatch";
    public const string DeliveriesTrack = "deliveries.track";
    public const string BatchesManage = "batches.manage";
    public const string ReturnsManage = "returns.manage";
    public const string ReturnsQc = "returns.qc";

    // Targets and dashboards
    public const string TargetsView = "targets.view";
    public const string TargetsManage = "targets.manage";
    public const string DashboardsView = "dashboards.view";

    // Every constant above, collected by reflection so this list can never go out of sync.
    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}