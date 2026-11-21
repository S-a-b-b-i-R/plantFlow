import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { useOpenedCount } from "./useCustomers";

// For sales reps: how many customers opened up since they last looked at the open list,
// shown next to Customers in the menu. Nothing when none did.
export function OpenedBadge() {
    const me = useMe().data!;
    const isRep =
        can(me, PERMISSIONS.customersManage) &&
        !can(me, PERMISSIONS.customersAssign);
    const opened = useOpenedCount(isRep);
    const count = opened.data?.count ?? 0;
    if (count === 0) return null;

    return (
        <span
            className="grid h-5 min-w-5 place-items-center rounded-full bg-signal px-1.5 text-xs font-bold text-ink"
            aria-label={`${count} newly open`}
        >
            {count}
        </span>
    );
}
