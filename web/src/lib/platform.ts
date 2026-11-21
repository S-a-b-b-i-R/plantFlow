import type { UserStatus } from "./types";

export interface CompanySummary {
    id: string;
    name: string;
    slug: string;
    status: "Active" | "Suspended";
    userCount: number;
    pendingCount: number;
    adminCount: number;
    createdAtUtc: string | null;
}

export interface UnassignedUser {
    id: string;
    email: string;
    displayName: string | null;
    status: UserStatus;
    isPlatformAdmin: boolean;
    createdAtUtc: string;
}

// "Acme Concrete Ltd." becomes "acme-concrete-ltd": lowercase letters, digits and single hyphens.
export const slugFrom = (name: string) =>
    name
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 60)
        .replace(/-+$/, "");
