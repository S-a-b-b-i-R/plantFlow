import type { Me } from "@/lib/types";

// The permission keys the web app checks. They are the same strings the API uses.
export const PERMISSIONS = {
    dashboardsView: "dashboards.view",
};

export const can = (me: Me, permission: string) =>
    me.permissions.includes(permission);
