import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { usePendingCloses } from "./useLedger";

// How many credit closes wait for this approver, shown next to Ledger in the menu. Their own requests
// don't count, since someone else has to decide them. Nothing when none wait.
export function CloseBadge() {
    const me = useMe().data!;
    const closes = usePendingCloses(can(me, PERMISSIONS.ledgerApprove));
    const count = closes.data?.filter((c) => !c.requestedByMe).length ?? 0;
    if (count === 0) return null;

    return (
        <span
            className="grid h-5 min-w-5 place-items-center rounded-full bg-signal px-1.5 text-xs font-bold text-ink"
            aria-label={`${count} to approve`}
        >
            {count}
        </span>
    );
}
