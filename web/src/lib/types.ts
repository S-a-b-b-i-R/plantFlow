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

export interface OperatorLoad {
    id: string;
    pourId: string;
    number: number;
    truckId: string;
    quantityCft: number;
    quantityReason: string | null;
    stage: string;
    producedAtUtc: string;
    dispatchedAtUtc: string | null;
    arrivedAtUtc: string | null;
    pouringStartedAtUtc: string | null;
    pouringEndedAtUtc: string | null;
    returnedAtUtc: string | null;
    cancelledAtUtc: string | null;
    cancelReason: string | null;
    returnedCft: number;
    rejectedAtUtc: string | null;
    rejectReason: string | null;
    sourceReturnId: string | null;
}

export interface OperatorPour {
    pourId: string;
    orderId: string;
    orderLineId: string;
    siteId: string;
    plannedCft: number;
    plannedStart: string;
    pumpId: string | null;
    pumpDispatchedAtUtc: string | null;
    pumpArrivedAtUtc: string | null;
    pumpAssembledAtUtc: string | null;
    producedCft: number;
    remainingCft: number;
    deliveredCft: number;
    loads: OperatorLoad[];
}

export interface ShiftProduction {
    planId: string;
    plantId: string;
    date: string;
    shiftId: string;
    status: "Draft" | "Submitted" | "Approved";
    plannedCft: number;
    producedCft: number;
    remainingCft: number;
    deliveredCft: number;
    pours: OperatorPour[];
}

export interface TruckOption {
    id: string;
    code: string;
    plateNumber: string | null;
    capacityCft: number;
    busy: boolean;
}
