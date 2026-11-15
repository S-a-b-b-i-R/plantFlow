export type UserStatus = "Pending" | "Active" | "Rejected" | "Disabled";

export interface Me {
    id: string;
    email: string;
    displayName: string | null;
    status: UserStatus;
    isPlatformAdmin: boolean;
    tenantId: string;
    tenantName: string;
    tenantSlug: string;
    permissions: string[];
}

// A pour step against its plan. Delay is null until the step is due.
export interface StepDelay {
    plannedUtc: string;
    actualUtc: string | null;
    delayMinutes: number | null;
    late: boolean;
}

export interface LoadCounts {
    waiting: number;
    onTheRoad: number;
    atSite: number;
    pouring: number;
    returning: number;
    done: number;
}

export interface LivePour {
    pourId: string;
    orderId: string;
    pumpId: string | null;
    plannedCft: number;
    // Plant-local time without a zone, for example "2026-06-01T09:00:00".
    plannedStart: string;
    producedCft: number;
    remainingCft: number;
    deliveredCft: number;
    loads: LoadCounts;
    pumpArrivedAtUtc: string | null;
    pumpDispatch: StepDelay | null;
    pumpReady: StepDelay | null;
    firstTruck: StepDelay | null;
    pourStart: StepDelay | null;
    late: boolean;
    customerName: string;
    siteName: string;
}

export interface LivePlan {
    planId: string;
    plantId: string;
    plantName: string;
    shiftId: string;
    shiftName: string;
    status: "Draft" | "Submitted" | "Approved";
    plannedCft: number;
    producedCft: number;
    remainingCft: number;
    deliveredCft: number;
    loads: LoadCounts;
    latePours: number;
    pours: LivePour[];
}

export interface LiveDashboard {
    date: string;
    lateThresholdMinutes: number;
    asOfUtc: string;
    plannedCft: number;
    producedCft: number;
    deliveredCft: number;
    loads: LoadCounts;
    latePours: number;
    plans: LivePlan[];
}
