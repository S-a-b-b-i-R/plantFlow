// Shapes and wording for shift planning. The field names are the API's.
import type { PourElement, PourMethod } from "./orders";

export type ShiftPlanStatus = "Draft" | "Submitted" | "Approved";

export const planStatusLabel: Record<ShiftPlanStatus, string> = {
    Draft: "Draft",
    Submitted: "Waiting for the plant head",
    Approved: "Approved",
};

export interface PlantShift {
    id: string;
    plantId: string;
    name: string;
    // "06:00:00"
    startTime: string;
    endTime: string;
    crossesMidnight: boolean;
    durationMinutes: number;
    status: "Active" | "Inactive";
}

export interface ShiftPlanSummary {
    id: string;
    plantId: string;
    plantCode: string;
    date: string;
    shiftId: string;
    shiftName: string;
    shiftStart: string;
    shiftEnd: string;
    status: ShiftPlanStatus;
    pourCount: number;
    totalPlannedCft: number;
    returnReason: string | null;
    submittedAtUtc: string | null;
    approvedAtUtc: string | null;
}

export interface Pour {
    id: string;
    orderId: string;
    orderNumber: string;
    customerName: string;
    siteName: string;
    orderLineId: string;
    productCode: string;
    pourMethod: PourMethod;
    plannedCft: number;
    // Plant-local time without a zone, for example "2026-06-01T09:00:00".
    plannedStart: string;
    pumpId: string | null;
    pumpCode: string | null;
    operatorId: string | null;
    operatorName: string | null;
    crew: string[];
    crewNotes: string | null;
    pumpLeaveAt: string | null;
    firstTruckLeaveAt: string;
    estimatedTruckLoads: number;
    restOverrideReason: string | null;
    notes: string | null;
}

export interface ShiftPlanDetail {
    plan: ShiftPlanSummary;
    pours: Pour[];
}

export interface LineToPlan {
    orderId: string;
    orderNumber: string;
    customerName: string;
    siteName: string;
    requestedDate: string;
    orderLineId: string;
    productCode: string;
    pourMethod: PourMethod;
    pourElement: PourElement;
    quantityCft: number;
    plannedCft: number;
    remainingCft: number;
    overdue: boolean;
}

export interface PumpOption {
    id: string;
    plantId: string;
    code: string;
    type: "Pump" | "TransitMixer";
    status: "Available" | "InService" | "Maintenance" | "Retired";
    defaultDriverName: string | null;
}

export interface DriverOption {
    id: string;
    name: string;
}

export const hhmm = (time: string) => time.slice(0, 5);

// Today and the days around it, as the browser's calendar dates (yyyy-mm-dd).
export const isoDate = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

export const todayIso = () => isoDate(new Date());

export function addDays(date: string, days: number) {
    const d = new Date(`${date}T12:00:00`);
    d.setDate(d.getDate() + days);
    return isoDate(d);
}

// At most two decimals, more than zero.
export const validQuantity = (text: string) => {
    const n = Number(text);
    return (
        text.trim() !== "" &&
        Number.isFinite(n) &&
        n > 0 &&
        Math.round(n * 100) / 100 === n
    );
};
