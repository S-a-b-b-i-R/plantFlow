export type TargetLevel = "Company" | "Plant" | "Rep";

// One company, plant or rep in a quarter. billedCft is final (closed orders). provisionalCft is delivered
// volume on orders still open, only for the quarter running today.
export interface TargetRow {
    level: TargetLevel;
    plantId: string | null;
    salesRepId: string | null;
    name: string;
    code: string | null;
    targetId: string | null;
    targetCft: number | null;
    billedCft: number;
    provisionalCft: number;
}

// endDate is the quarter's last day. A sales rep gets only their own row in reps, and no company or plants.
export interface TargetQuarter {
    year: number;
    quarter: number;
    label: string;
    startDate: string;
    endDate: string;
    current: boolean;
    ended: boolean;
    yearStartMonth: number;
    company: TargetRow | null;
    plants: TargetRow[];
    reps: TargetRow[];
    warnings: string[];
}

export const previousQuarter = (year: number, quarter: number) =>
    quarter === 1
        ? { year: year - 1, quarter: 4 }
        : { year, quarter: quarter - 1 };

export const nextQuarter = (year: number, quarter: number) =>
    quarter === 4
        ? { year: year + 1, quarter: 1 }
        : { year, quarter: quarter + 1 };

// Whole percent of the target reached, or null without a target.
export const percentOf = (volume: number, target: number | null) =>
    target ? Math.round((volume / target) * 100) : null;
