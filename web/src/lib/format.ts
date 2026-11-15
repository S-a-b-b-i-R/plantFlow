export const cft = (n: number) =>
    n.toLocaleString(undefined, { maximumFractionDigits: 2 });

// A plant-local time such as "2026-06-01T09:00:00" is shown as written, with no time-zone shift.
export const plantTime = (local: string) => local.slice(11, 16);

// A UTC time shown on the viewer's clock.
export const clock = (iso: string) =>
    new Date(iso).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
    });
