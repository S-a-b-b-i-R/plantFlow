using static PlantFlow.Api.Domain.Security.Permissions;

namespace PlantFlow.Api.Domain.Security;

// The roles every new company starts with. Admins can change them later.
public static class DefaultRoles
{
    public const string AdminRoleName = "Admin";
    public static readonly IReadOnlyDictionary<string, string[]> All = new Dictionary<string, string[]>
    {
        [AdminRoleName] = Permissions.All.ToArray(),

        ["Sales Representative"] =
        [
            MasterDataView, MixDesignsView, PricesView, OrdersView, OrdersCreate, OrdersEdit,
            SiteSurveysView, DeliveriesTrack, TargetsView, DashboardsView
        ],

        ["Sales Manager"] =
        [
            MasterDataView, MixDesignsView, PricesView, OrdersView, OrdersCreate, OrdersEdit,
            OrdersApproveSalesManager, OrdersConfirm, SiteSurveysView, DeliveriesTrack,
            TargetsView, TargetsManage, LedgerView, DashboardsView
        ],

        ["Accounts"] =
        [
            MasterDataView, MixDesignsView, CostsManage, PricesView, PricesEdit, PricesApprove,
            OrdersView, OrdersBillingResolve, LedgerView, LedgerManage,
            TargetsView, DashboardsView
        ],

        ["Technical"] =
        [
            MasterDataView, MixDesignsView, MixDesignsEdit, MixDesignsApprove, OrdersView,
            OrdersApproveTechnical, SiteSurveysView, SiteSurveysPerform, SiteSurveysAttach,
            DeliveriesTrack, ReturnsQc, DashboardsView
        ],

        ["Credit"] = [OrdersView, OrdersApproveCredit, LedgerView, DashboardsView],

        ["Planner"] =
        [
            MasterDataView, OrdersView, OrdersClose, SiteSurveysView, SiteSurveysAttach,
            DeliveriesPlan, DeliveriesTrack, BatchesManage, ReturnsManage, DashboardsView
        ],

        ["Dispatcher"] = [OrdersView, DeliveriesDispatch, DeliveriesTrack, ReturnsManage, DashboardsView],

        ["Batching Plant Operator"] = [OrdersView, DeliveriesDispatch, DeliveriesTrack, BatchesManage],

        ["Plant Manager"] =
        [
            MasterDataView, OrdersView, DeliveriesTrack, BatchesManage, ReturnsManage,
            TargetsView, DashboardsView
        ],

        ["Management"] = [OrdersView, OrdersApproveManagement, DeliveriesTrack, TargetsView, DashboardsView]
    };
}