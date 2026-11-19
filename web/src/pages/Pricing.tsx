import { Navigate, NavLink, useParams } from "react-router-dom";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { panelClass } from "@/components/orders/styles";
import { kinds } from "@/components/pricing/kinds";
import { MarginReport } from "@/components/pricing/MarginReport";
import { PendingSection, PricingList } from "@/components/pricing/PricingList";
import { usePendingTotal } from "@/components/pricing/useBook";
import type { PriceBookKind } from "@/components/pricing/kind";
import { cn } from "@/lib/utils";

const tabClass = ({ isActive }: { isActive: boolean }) =>
    cn(
        "inline-flex h-10 items-center gap-2 rounded-sm border px-4 text-sm font-semibold whitespace-nowrap",
        isActive
            ? "border-signal bg-signal text-ink"
            : "bg-card hover:border-ink",
    );

// The five kinds of row that make up the price book, one tab each, an inbox of what waits for this person, and the cost and margin report.
export function Pricing() {
    const me = useMe().data!;
    const { tab } = useParams();

    const visible = kinds.filter((k) => k.see.some((p) => can(me, p)));
    const deciding = visible.filter((k) => can(me, k.approve));

    if (visible.length === 0) return <Navigate to="/" replace />;

    const hasInbox = deciding.length > 0;
    const hasMargins = can(me, PERMISSIONS.costsManage);
    const current = visible.find((k) => k.key === tab);
    const known =
        tab === "approvals"
            ? hasInbox
            : tab === "margins"
              ? hasMargins
              : !!current;
    if (!known) {
        return (
            <Navigate
                to={`/pricing/${hasInbox ? "approvals" : visible[0].key}`}
                replace
            />
        );
    }

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Pricing</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    Recipes, costs and prices only count in a price check once
                    they are approved. Change one by adding a new row and ending
                    the old one.
                </p>
            </header>

            <nav
                aria-label="Pricing"
                className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:mx-0 sm:px-0"
            >
                {hasInbox && (
                    <NavLink to="/pricing/approvals" className={tabClass}>
                        Approvals
                        <PendingTotal kinds={deciding} />
                    </NavLink>
                )}
                {visible.map((k) => (
                    <NavLink
                        key={k.key}
                        to={`/pricing/${k.key}`}
                        className={tabClass}
                    >
                        {k.label}
                    </NavLink>
                ))}
                {hasMargins && (
                    <NavLink to="/pricing/margins" className={tabClass}>
                        Cost and margin
                    </NavLink>
                )}
            </nav>

            {current ? (
                <PricingList key={current.key} kind={current} />
            ) : tab === "margins" ? (
                <MarginReport />
            ) : (
                <Inbox kinds={deciding} />
            )}
        </div>
    );
}

// The count on the Approvals tab.
function PendingTotal({ kinds }: { kinds: PriceBookKind[] }) {
    const total = usePendingTotal(kinds);
    return total > 0 ? (
        <span className="rounded-sm bg-ink px-1.5 text-xs text-background">
            {total}
        </span>
    ) : null;
}

function Inbox({ kinds }: { kinds: PriceBookKind[] }) {
    const total = usePendingTotal(kinds);

    return (
        <div className="space-y-6">
            {total === 0 && (
                <p
                    className={cn(
                        panelClass,
                        "px-4 py-12 text-center text-sm text-muted-foreground",
                    )}
                >
                    Nothing is waiting for your approval.
                </p>
            )}
            {kinds.map((k) => (
                <PendingSection key={k.key} kind={k} />
            ))}
        </div>
    );
}
