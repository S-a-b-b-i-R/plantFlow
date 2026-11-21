import type { ActiveStatus } from "@/lib/plants";

export type VehicleType = "TransitMixer" | "Pump";

export type VehicleStatus =
    "Available" | "InService" | "Maintenance" | "Retired";

export const vehicleStatuses: VehicleStatus[] = [
    "Available",
    "InService",
    "Maintenance",
    "Retired",
];

export const vehicleStatusLabel: Record<VehicleStatus, string> = {
    Available: "Available",
    InService: "In service",
    Maintenance: "Maintenance",
    Retired: "Retired",
};

export interface Vehicle {
    id: string;
    plantId: string;
    plantCode: string;
    code: string;
    type: VehicleType;
    capacityCft: number | null;
    plateNumber: string | null;
    // Transit mixers only.
    defaultDriverId: string | null;
    defaultDriverName: string | null;
    status: VehicleStatus;
    assemblyMinutes: number | null;
    // Active pump operators on a pump's crew.
    crewCount: number;
}

export interface Driver {
    id: string;
    name: string;
    phone: string | null;
    status: ActiveStatus;
    // The mixers this driver usually drives.
    mixerCodes: string[];
}

export interface PumpOperator {
    id: string;
    name: string;
    // Null only for operators carried over from the old crew names.
    phone: string | null;
    pumpId: string | null;
    pumpCode: string | null;
    status: ActiveStatus;
}

// The body PUT /api/vehicles/{id} expects, built from a vehicle with some fields changed.
export const vehicleBody = (
    vehicle: Vehicle,
    changes: Partial<{
        plantId: string;
        code: string;
        capacityCft: number | null;
        plateNumber: string | null;
        defaultDriverId: string | null;
        status: VehicleStatus;
        assemblyMinutes: number | null;
    }> = {},
) => ({
    plantId: vehicle.plantId,
    code: vehicle.code,
    type: vehicle.type,
    capacityCft: vehicle.capacityCft,
    plateNumber: vehicle.plateNumber,
    defaultDriverId: vehicle.defaultDriverId,
    status: vehicle.status,
    assemblyMinutes: vehicle.assemblyMinutes,
    ...changes,
});

// The body PUT /api/pump-operators/{id} expects. Phone stays empty only when it was never entered,
// and then the API asks for it.
export const operatorBody = (
    operator: PumpOperator,
    changes: Partial<{
        name: string;
        phone: string;
        pumpId: string | null;
        status: ActiveStatus;
    }> = {},
) => ({
    name: operator.name,
    phone: operator.phone ?? "",
    pumpId: operator.pumpId,
    status: operator.status,
    ...changes,
});
