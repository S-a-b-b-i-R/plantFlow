import type { IconType } from "react-icons";
import { LuActivity, LuTruck } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import type { Me } from "@/lib/types";

export type NavItem = {
    to: string;
    label: string;
    icon: IconType;
    // Every one of these is needed. The batching console also reads the live board.
    permissions: string[];
};

export const navigation: NavItem[] = [
    {
        to: "/live",
        label: "Live operations",
        icon: LuActivity,
        permissions: [PERMISSIONS.dashboardsView],
    },
    {
        to: "/operator",
        label: "Batching",
        icon: LuTruck,
        permissions: [PERMISSIONS.batchesManage, PERMISSIONS.dashboardsView],
    },
];

export const canOpen = (me: Me, item: NavItem) =>
    item.permissions.every((permission) => can(me, permission));

// Where someone lands: the first screen they are allowed to use.
export const firstAllowed = (me: Me) =>
    navigation.find((item) => canOpen(me, item))?.to ?? null;
