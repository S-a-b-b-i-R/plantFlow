import { Link } from "react-router-dom";
import { OpButton as Button } from "@/components/operator/OpButton";
import { ReasonAction } from "@/components/orders/ReasonAction";
import { panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import { closeReasonLabel } from "@/lib/ledger";
import { dateTime, money } from "@/lib/orders";
import { cn } from "@/lib/utils";
import { useLedgerAct, usePendingCloses } from "./useLedger";

// Credit closes waiting for a decision. Nobody decides a close they asked for themselves.
export function CloseQueue() {
    const closes = usePendingCloses();
    const act = useLedgerAct();

    if (closes.isLoading) return <Skeleton className="h-32 w-full" />;
    if (closes.isError) {
        return (
            <p role="alert" className="text-sm text-late">
                {closes.error.message}
            </p>
        );
    }

    const items = closes.data ?? [];
    if (items.length === 0) {
        return (
            <p className={cn(panelClass, "p-4 text-sm text-muted-foreground")}>
                No credit is waiting to close. Accounts asks here when a
                customer is refunded or no longer needs their credit.
            </p>
        );
    }

    return (
        <ul className="grid items-start gap-3 lg:grid-cols-2">
            {items.map((c) => (
                <li key={c.id} className={cn(panelClass, "space-y-3 p-4")}>
                    <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="min-w-0">
                            <Link
                                to={`/ledger/${c.customerId}`}
                                className="font-semibold underline-offset-2 hover:underline"
                            >
                                {c.customerName}
                            </Link>
                            <p className="text-sm">
                                {closeReasonLabel[c.reason]}: {c.note}
                            </p>
                            <p className="text-xs text-muted-foreground">
                                Asked by {c.requestedByName ?? "someone"},{" "}
                                {dateTime(c.requestedAtUtc)}
                            </p>
                        </div>
                        <div className="sm:text-right">
                            <p className="font-semibold tabular-nums">
                                {cft(c.quantityCft)} CFT {c.productCode}
                            </p>
                            <p className="text-xs text-muted-foreground tabular-nums">
                                {money(c.value)} at {money(c.unitPrice)} per CFT
                            </p>
                        </div>
                    </div>
                    {c.requestedByMe ? (
                        <p className="border-t pt-3 text-sm text-muted-foreground">
                            You asked for this, so someone else has to decide
                            it.
                        </p>
                    ) : (
                        <div className="flex flex-wrap gap-2 border-t pt-3">
                            {/* Dark, not yellow: a list of equal items has no single next action. */}
                            <Button
                                className="h-11 px-5"
                                disabled={act.isPending}
                                onClick={() =>
                                    act.mutate({
                                        url: `/api/ledger/closes/${c.id}/approve`,
                                    })
                                }
                            >
                                Approve
                            </Button>
                            <ReasonAction
                                label="Send back"
                                question="Why should the credit stay open?"
                                submitLabel="Send back"
                                tone="danger"
                                className="h-11 px-4"
                                pending={act.isPending}
                                onSubmit={(reason) =>
                                    act.mutate({
                                        url: `/api/ledger/closes/${c.id}/send-back`,
                                        body: { reason },
                                    })
                                }
                            />
                        </div>
                    )}
                </li>
            ))}
            {act.isError && (
                <p role="alert" className="text-sm text-late">
                    {act.error.message}
                </p>
            )}
        </ul>
    );
}
