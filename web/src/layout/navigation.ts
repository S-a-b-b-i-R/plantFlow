import type { IconType } from "react-icons";
import {
    LuActivity,
    LuBriefcase,
    LuBuilding2,
    LuCalendarDays,
    LuCarFront,
    LuPackage,
    LuClipboardList,
    LuFactory,
    LuFlaskConical,
    LuMapPinned,
    LuTags,
    LuTruck,
    LuUsers,
} from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import type { Me } from "@/lib/types";

// The sidebar shows the groups in this order. A group with nothing the person can open is left out.
export const navGroups = ["Operations", "Sales", "Finance", "Setup"] as const;
export type NavGroup = (typeof navGroups)[number];

export type NavItem = {
    to: string;
    group: NavGroup;
    label: string;
    icon: IconType;
    // Every one of these is needed. The batching console also reads the live board.
    permissions: string[];
    // When set, one of these is also needed.
    anyOf?: string[];
};

export const navigation: NavItem[] = [
    {
        to: "/live",
        group: "Operations",
        label: "Live operations",
        icon: LuActivity,
        permissions: [PERMISSIONS.dashboardsView],
    },
    {
        to: "/orders",
        group: "Sales",
        label: "Orders",
        icon: LuClipboardList,
        permissions: [PERMISSIONS.ordersView],
    },
    {
        // The pickers read plants, shifts, pumps and drivers, which need master data access.
        to: "/planning",
        group: "Operations",
        label: "Planning",
        icon: LuCalendarDays,
        permissions: [PERMISSIONS.masterDataView],
        anyOf: [PERMISSIONS.deliveriesPlan, PERMISSIONS.deliveriesPlanApprove],
    },
    {
        // Finding a site needs the customer and site lists, which are master data.
        to: "/surveys",
        group: "Sales",
        label: "Surveys",
        icon: LuMapPinned,
        permissions: [PERMISSIONS.siteSurveysView, PERMISSIONS.masterDataView],
    },
    {
        to: "/pricing",
        group: "Finance",
        label: "Pricing",
        icon: LuTags,
        permissions: [],
        anyOf: [
            PERMISSIONS.pricesView,
            PERMISSIONS.mixDesignsView,
            PERMISSIONS.costsManage,
            PERMISSIONS.costsApprove,
        ],
    },
    {
        to: "/operator",
        group: "Operations",
        label: "Batching",
        icon: LuTruck,
        permissions: [PERMISSIONS.batchesManage, PERMISSIONS.dashboardsView],
    },
    {
        to: "/quality",
        group: "Operations",
        label: "Quality",
        icon: LuFlaskConical,
        permissions: [PERMISSIONS.returnsQc],
    },
    {
        // Everyone with master data access can look; customers.manage is needed to change anything.
        to: "/customers",
        group: "Sales",
        label: "Customers",
        icon: LuBuilding2,
        permissions: [PERMISSIONS.masterDataView],
    },
    {
        // Managing reps and their logins is for sales managers.
        to: "/sales-reps",
        group: "Sales",
        label: "Sales reps",
        icon: LuBriefcase,
        permissions: [PERMISSIONS.masterDataView, PERMISSIONS.customersAssign],
    },
    {
        // Everyone with master data access can look; plants.manage is needed to change anything.
        to: "/plants",
        group: "Setup",
        label: "Plants",
        icon: LuFactory,
        permissions: [PERMISSIONS.masterDataView],
    },
    {
        // Everyone with master data access can look; fleet.manage is needed to change anything.
        to: "/fleet",
        group: "Setup",
        label: "Fleet",
        icon: LuCarFront,
        permissions: [PERMISSIONS.masterDataView],
    },
    {
        // Everyone with master data access can look; Technical edits products and materials, Accounts edits charges.
        to: "/catalogue",
        group: "Setup",
        label: "Catalogue",
        icon: LuPackage,
        permissions: [PERMISSIONS.masterDataView],
    },
    {
        // Assigning plants reads the plant list, which is master data.
        to: "/users",
        group: "Setup",
        label: "Users",
        icon: LuUsers,
        permissions: [PERMISSIONS.usersManage, PERMISSIONS.masterDataView],
    },
];

export const canOpen = (me: Me, item: NavItem) =>
    item.permissions.every((permission) => can(me, permission)) &&
    (!item.anyOf || item.anyOf.some((permission) => can(me, permission)));

// Where someone lands. People who sell, approve or bill start on orders, everyone else on the first screen they can use.
export const firstAllowed = (me: Me) => {
    const office = [
        PERMISSIONS.ordersCreate,
        PERMISSIONS.ordersApproveSalesManager,
        PERMISSIONS.ordersApproveManagement,
        PERMISSIONS.ordersApproveCredit,
        PERMISSIONS.ordersBillingResolve,
    ].some((permission) => can(me, permission));
    const preferred = office
        ? navigation.find((item) => item.to === "/orders")
        : undefined;
    const item =
        preferred && canOpen(me, preferred)
            ? preferred
            : navigation.find((entry) => canOpen(me, entry));
    return item?.to ?? null;
};
