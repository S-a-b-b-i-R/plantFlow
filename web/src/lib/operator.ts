import type { OperatorLoad, OperatorPour } from "./types";

// The API sends UTC times. One without a zone mark is read as UTC, never as local time.
export function utcMs(iso: string): number {
    return Date.parse(/(Z|[+-]\d\d:\d\d)$/.test(iso) ? iso : `${iso}Z`);
}

export function minutesSince(iso: string, now: number): number {
    return Math.max(0, Math.floor((now - utcMs(iso)) / 60_000));
}

export type LoadState =
    | "ready"
    | "road"
    | "site"
    | "pouring"
    | "back"
    | "done"
    | "cancelled"
    | "rejected";

export function loadState(load: OperatorLoad): LoadState {
    if (load.cancelledAtUtc) return "cancelled";
    if (load.rejectedAtUtc) return "rejected";
    if (load.returnedAtUtc) return "done";
    if (load.pouringEndedAtUtc) return "back";
    if (load.pouringStartedAtUtc) return "pouring";
    if (load.arrivedAtUtc) return "site";
    if (load.dispatchedAtUtc) return "road";
    return "ready";
}

export const stateLabel: Record<LoadState, string> = {
    ready: "Ready to leave",
    road: "On the road",
    site: "At site",
    pouring: "Pouring",
    back: "Heading back",
    done: "Done",
    cancelled: "Cancelled",
    rejected: "Rejected",
};

// When the load entered its current state.
export function stateSince(load: OperatorLoad): string | null {
    switch (loadState(load)) {
        case "ready":
            return load.producedAtUtc;
        case "road":
            return load.dispatchedAtUtc;
        case "site":
            return load.arrivedAtUtc;
        case "pouring":
            return load.pouringStartedAtUtc;
        case "back":
            return load.pouringEndedAtUtc;
        default:
            return null;
    }
}

export interface Action {
    label: string;
    url: string;
    body: Record<string, unknown>;
}

// The one thing to do next with a load.
export function loadAction(load: OperatorLoad): Action | null {
    const step = (label: string, name: string): Action => ({
        label,
        url: `/api/loads/${load.id}/steps`,
        body: { step: name },
    });
    switch (loadState(load)) {
        case "ready":
            return {
                label: "Send out",
                url: `/api/loads/${load.id}/dispatch`,
                body: {},
            };
        case "road":
            return step("Arrived at site", "Arrived");
        case "site":
            return step("Pouring started", "PouringStarted");
        case "pouring":
            return step("Pouring ended", "PouringEnded");
        case "back":
            return step("Truck is back", "Returned");
        default:
            return null;
    }
}

export const isOut = (load: OperatorLoad) =>
    ["ready", "road", "site", "pouring", "back"].includes(loadState(load));

// The next pump step, or null once the pump is ready.
export function pumpAction(pour: OperatorPour): Action | null {
    const step = (label: string, name: string): Action => ({
        label,
        url: `/api/pours/${pour.pourId}/pump-steps`,
        body: { step: name },
    });
    if (!pour.pumpDispatchedAtUtc) return step("Pump left", "Dispatched");
    if (!pour.pumpArrivedAtUtc) return step("Pump arrived", "Arrived");
    if (!pour.pumpAssembledAtUtc) return step("Pump ready", "Assembled");
    return null;
}
