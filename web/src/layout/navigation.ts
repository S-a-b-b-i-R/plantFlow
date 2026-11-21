import type { IconType } from "react-icons";
import {
    LuActivity,
    LuCalendarDays,
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

export type NavItem = {
    to: string;
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
        label: "Live operations",
        icon: LuActivity,
        permissions: [PERMISSIONS.dashboardsView],
    },
    {
        to: "/orders",
        label: "Orders",
        icon: LuClipboardList,
        permissions: [PERMISSIONS.ordersView],
    },
    {
        // The pickers read plants, shifts, pumps and drivers, which need master data access.
        to: "/planning",
        label: "Planning",
        icon: LuCalendarDays,
        permissions: [PERMISSIONS.masterDataView],
        anyOf: [PERMISSIONS.deliveriesPlan, PERMISSIONS.deliveriesPlanApprove],
    },
    {
        // Finding a site needs the customer and site lists, which are master data.
        to: "/surveys",
        label: "Surveys",
        icon: LuMapPinned,
        permissions: [PERMISSIONS.siteSurveysView, PERMISSIONS.masterDataView],
    },
    {
        to: "/pricing",
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
        label: "Batching",
        icon: LuTruck,
        permissions: [PERMISSIONS.batchesManage, PERMISSIONS.dashboardsView],
    },
    {
        to: "/quality",
        label: "Quality",
        icon: LuFlaskConical,
        permissions: [PERMISSIONS.returnsQc],
    },
    {
        // Everyone with master data access can look; plants.manage is needed to change anything.
        to: "/plants",
        label: "Plants",
        icon: LuFactory,
        permissions: [PERMISSIONS.masterDataView],
    },
    {
        // Assigning plants reads the plant list, which is master data.
        to: "/users",
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
