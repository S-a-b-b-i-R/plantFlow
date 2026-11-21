import type { Me } from "@/lib/types";

// The permission keys the web app checks. They are the same strings the API uses.
export const PERMISSIONS = {
    dashboardsView: "dashboards.view",
    batchesManage: "batches.manage",
    deliveriesTrack: "deliveries.track",
    deliveriesPlan: "deliveries.plan",
    deliveriesPlanApprove: "deliveries.plan.approve",
    deliveriesPlanOverride: "deliveries.plan.override",
    returnsManage: "returns.manage",
    returnsQc: "returns.qc",
    masterDataView: "masterdata.view",
    plantsManage: "plants.manage",
    customersManage: "customers.manage",
    customersAssign: "customers.assign",
    customersVat: "customers.vat",
    pricesView: "prices.view",
    pricesEdit: "prices.edit",
    pricesApprove: "prices.approve",
    mixDesignsView: "mixdesigns.view",
    mixDesignsEdit: "mixdesigns.edit",
    mixDesignsApprove: "mixdesigns.approve",
    costsManage: "costs.manage",
    costsApprove: "costs.approve",
    siteSurveysView: "sitesurveys.view",
    siteSurveysPerform: "sitesurveys.perform",
    siteSurveysAttach: "sitesurveys.attach",
    siteSurveysApprove: "sitesurveys.approve",
    ordersView: "orders.view",
    ordersCreate: "orders.create",
    ordersEdit: "orders.edit",
    ordersApproveSalesManager: "orders.approve.salesmanager",
    ordersApproveManagement: "orders.approve.management",
    ordersApproveCredit: "orders.approve.credit",
    ordersConfirm: "orders.confirm",
    ordersClose: "orders.close",
    ordersBillingResolve: "orders.billing.resolve",
    usersManage: "users.manage",
};

export const can = (me: Me, permission: string) =>
    me.permissions.includes(permission);
