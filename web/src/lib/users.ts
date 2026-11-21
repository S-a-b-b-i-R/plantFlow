import type { UserStatus } from "@/lib/types";

export interface UserPlant {
    id: string;
    code: string;
    name: string;
}

export interface UserSummary {
    id: string;
    email: string;
    displayName: string | null;
    status: UserStatus;
    isPlatformAdmin: boolean;
    roles: string[];
    approvedAtUtc: string | null;
    plants: UserPlant[];
}

export interface PermissionOverride {
    permissionKey: string;
    isGranted: boolean;
}

export interface UserDetail {
    id: string;
    email: string;
    displayName: string | null;
    status: UserStatus;
    roleIds: string[];
    permissionOverrides: PermissionOverride[];
    plantIds: string[];
}

export interface RoleOption {
    id: string;
    name: string;
    permissions: string[];
    // One of PlantFlow's roles: can be renamed, not deleted.
    isBuiltIn: boolean;
    // The Admin role: always every permission, can't be changed.
    isLocked: boolean;
    userCount: number;
}

export const statusLabel: Record<UserStatus, string> = {
    Pending: "Waiting for approval",
    Active: "Active",
    Disabled: "Disabled",
    Rejected: "Rejected",
};
