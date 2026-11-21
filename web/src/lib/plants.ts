export type ActiveStatus = "Active" | "Inactive";

export interface PlantSummary {
    id: string;
    code: string;
    name: string;
    address: string | null;
    timeZone: string;
    operatingHoursPerDay: number;
    status: ActiveStatus;
    batchingPlantCount: number;
    dailyCapacityCft: number;
}

export interface BatchingPlant {
    id: string;
    plantId: string;
    code: string;
    make: string | null;
    model: string | null;
    ratedCapacityCftPerHour: number;
    batchDurationMinutes: number;
    status: ActiveStatus;
}

export interface PlantDetail {
    plant: PlantSummary;
    batchingPlants: BatchingPlant[];
}

export interface PlantShift {
    id: string;
    plantId: string;
    name: string;
    // "HH:mm:ss", plant-local.
    startTime: string;
    endTime: string;
    crossesMidnight: boolean;
    durationMinutes: number;
    status: ActiveStatus;
}

export interface PlantUsage {
    openOrders: number;
    upcomingPours: number;
    assignedUsers: number;
}

export const codePattern = /^[A-Za-z0-9-]{1,20}$/;

// "06:00:00" → "06:00"
export const hhmm = (time: string) => time.slice(0, 5);

// A <input type="time"> gives "06:00"; the API wants "06:00:00".
export const withSeconds = (time: string) =>
    time.length === 5 ? `${time}:00` : time;

export const hoursLabel = (minutes: number) => {
    const h = Math.floor(minutes / 60);
    const m = minutes % 60;
    return m === 0 ? `${h} h` : `${h} h ${m} min`;
};

// What still relies on a plant, in words. Empty when nothing does.
export function usageParts(usage: PlantUsage): string[] {
    const parts: string[] = [];
    const n = (count: number, one: string, many: string) =>
        `${count} ${count === 1 ? one : many}`;
    if (usage.openOrders)
        parts.push(n(usage.openOrders, "open order", "open orders"));
    if (usage.upcomingPours)
        parts.push(
            n(usage.upcomingPours, "pour planned ahead", "pours planned ahead"),
        );
    if (usage.assignedUsers)
        parts.push(n(usage.assignedUsers, "assigned user", "assigned users"));
    return parts;
}
