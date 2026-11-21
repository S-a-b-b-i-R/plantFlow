import { usePendingCount } from "./useUsers";

// How many people signed up and wait for approval, shown next to Users in the menu. Nothing when none wait.
export function PendingUsersBadge() {
    const pending = usePendingCount();
    const count = pending.data?.count ?? 0;
    if (count === 0) return null;

    return (
        <span
            className="grid h-5 min-w-5 place-items-center rounded-full bg-signal px-1.5 text-xs font-bold text-ink"
            aria-label={`${count} waiting for approval`}
        >
            {count}
        </span>
    );
}
