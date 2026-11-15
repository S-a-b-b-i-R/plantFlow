import type { LivePlan, LivePour, StepDelay } from "./types";

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;

export interface Axis {
    start: number;
    end: number;
}

export interface StepRow {
    key: string;
    label: string;
    step: StepDelay;
}

// The steps a pour goes through, in order. A pour without a pump has no pump steps.
export function stepRows(pour: LivePour): StepRow[] {
    const rows: [string, string, StepDelay | null][] = [
        ["pumpDispatch", "Pump leaves", pour.pumpDispatch],
        ["pumpReady", "Pump ready", pour.pumpReady],
        ["firstTruck", "First truck leaves", pour.firstTruck],
        ["pourStart", "Pour starts", pour.pourStart],
    ];
    return rows.flatMap(([key, label, step]) =>
        step ? [{ key, label, step }] : [],
    );
}

// From just before the earliest planned step to two hours after the last one.
// While the shift is running the axis stretches to keep the Now line in view.
export function planAxis(plan: LivePlan, now: number): Axis | null {
    const planned = plan.pours.flatMap((pour) =>
        stepRows(pour).map((row) => Date.parse(row.step.plannedUtc)),
    );
    if (planned.length === 0) return null;

    const half = 30 * MINUTE;
    const lastPlanned = Math.max(...planned);
    const start =
        Math.floor((Math.min(...planned) - 20 * MINUTE) / half) * half;

    let end = lastPlanned + 2 * HOUR;
    if (now > lastPlanned && now < lastPlanned + 8 * HOUR)
        end = Math.max(end, now + 20 * MINUTE);

    return { start, end: Math.ceil(end / half) * half };
}

// Position on the axis as a percentage. A time outside the axis sits on the edge and says which side.
export function place(axis: Axis, time: number) {
    const raw = ((time - axis.start) / (axis.end - axis.start)) * 100;
    return {
        pct: Math.min(100, Math.max(0, raw)),
        outside:
            raw < 0
                ? ("before" as const)
                : raw > 100
                  ? ("after" as const)
                  : null,
    };
}

export function ticks(axis: Axis): number[] {
    const step = axis.end - axis.start > 10 * HOUR ? 2 * HOUR : HOUR;
    const first = new Date(axis.start);
    first.setMinutes(0, 0, 0);

    const result: number[] = [];
    for (let t = first.getTime(); t <= axis.end; t += step) {
        if (t >= axis.start) result.push(t);
    }
    return result;
}
