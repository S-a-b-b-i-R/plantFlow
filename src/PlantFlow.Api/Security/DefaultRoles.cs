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
            SiteSurveysView, DeliveriesTrack, TargetsView, DashboardsView, CustomersManage
        ],

        ["Sales Manager"] =
        [
            MasterDataView, MixDesignsView, PricesView, OrdersView, OrdersCreate, OrdersEdit,
            OrdersApproveSalesManager, OrdersConfirm, SiteSurveysView, DeliveriesTrack,
            TargetsView, TargetsManage, LedgerView, DashboardsView, CustomersManage, CustomersAssign
        ],

        ["Accounts"] =
        [
            MasterDataView, MixDesignsView, CostsManage, PricesView, PricesEdit, PricesApprove,
            OrdersView, OrdersBillingResolve, LedgerView, LedgerManage,
            TargetsView, DashboardsView, CustomersVat, ChargesManage
        ],

        ["Technical"] =
        [
            MasterDataView, MixDesignsView, MixDesignsEdit, MixDesignsApprove, OrdersView,
            OrdersApproveTechnical, SiteSurveysView, SiteSurveysPerform, SiteSurveysAttach,
            DeliveriesTrack, ReturnsQc, DashboardsView, ProductsManage
        ],

        ["Credit"] = [OrdersView, OrdersApproveCredit, LedgerView, DashboardsView],

        ["Planner"] =
        [
            MasterDataView, OrdersView, OrdersClose, SiteSurveysView, SiteSurveysAttach,
            DeliveriesPlan, DeliveriesTrack, BatchesManage, ReturnsManage, DashboardsView
        ],

        ["Dispatcher"] = [OrdersView, DeliveriesDispatch, DeliveriesTrack, ReturnsManage, DashboardsView],

        ["Batching Plant Operator"] = [OrdersView, DeliveriesDispatch, DeliveriesTrack, BatchesManage, ReturnsManage, DashboardsView],

        ["Plant Manager"] =
        [
            MasterDataView, OrdersView, DeliveriesTrack, DeliveriesPlanApprove, DeliveriesPlanOverride,
            BatchesManage, ReturnsManage, TargetsView, DashboardsView, SiteSurveysView, SiteSurveysApprove,
            PlantsManage, FleetManage
        ],

        ["Management"] = [OrdersView, OrdersApproveManagement, CostsApprove, DeliveriesTrack, TargetsView, DashboardsView]
    };
}