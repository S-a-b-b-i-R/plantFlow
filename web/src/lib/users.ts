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
