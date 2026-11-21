import type { LoadCounts } from "./types";

export const cft = (n: number) =>
    n.toLocaleString(undefined, { maximumFractionDigits: 2 });

// A plant-local time such as "2026-06-01T09:00:00" is shown as written, with no time-zone shift.
export const plantTime = (local: string) => local.slice(11, 16);

const hoursAndMinutes: Intl.DateTimeFormatOptions = {
    hour: "2-digit",
    minute: "2-digit",
};

// A UTC time shown on the viewer's clock.
export const clock = (iso: string) =>
    new Date(iso).toLocaleTimeString([], hoursAndMinutes);
export const clockAt = (ms: number) =>
    new Date(ms).toLocaleTimeString([], hoursAndMinutes);
export const clockSeconds = (iso: string) =>
    new Date(iso).toLocaleTimeString([], {
        ...hoursAndMinutes,
        second: "2-digit",
    });

export const dayTitle = (date: string) =>
    new Date(`${date}T12:00:00`).toLocaleDateString([], {
        weekday: "long",
        day: "numeric",
        month: "long",
    });

export const signedMinutes = (n: number) =>
    n > 0 ? `+${n} min` : n < 0 ? `−${Math.abs(n)} min` : "on time";

// Where the trucks are, in words. Trucks that are done are left out.
export function loadParts(c: LoadCounts): string[] {
    const parts: string[] = [];
    if (c.waiting) parts.push(`${c.waiting} waiting to leave`);
    if (c.onTheRoad) parts.push(`${c.onTheRoad} on the road`);
    if (c.atSite) parts.push(`${c.atSite} at site`);
    if (c.pouring) parts.push(`${c.pouring} pouring`);
    if (c.returning) parts.push(`${c.returning} heading back`);
    return parts;
}

// Up to two letters for an avatar, from a name or an email address.
export const initialsOf = (name: string) =>
    name
        .split(/[\s@.]+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0].toUpperCase())
        .join("");
