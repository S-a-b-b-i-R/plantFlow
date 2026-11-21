import { useState } from "react";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Skeleton } from "@/components/ui/skeleton";
import { openOrdersText } from "@/lib/customers";
import type { ActiveStatus } from "@/lib/plants";
import { useCustomerSave, useOpenOrders } from "./useCustomers";

// Deactivate warns with the open orders that still use the record, then lets the person go ahead.
// Reactivate is one click.
export function DeactivateControl({
    noun,
    status,
    usagePath,
    savePath,
    bodyFor,
}: {
    noun: "customer" | "site";
    status: ActiveStatus;
    usagePath: string;
    savePath: string;
    bodyFor: (status: ActiveStatus) => unknown;
}) {
    const [confirming, setConfirming] = useState(false);
    const usage = useOpenOrders(usagePath, confirming);
    const save = useCustomerSave("", savePath);
    const setStatus = (next: ActiveStatus) =>
        save.mutate(bodyFor(next), { onSuccess: () => setConfirming(false) });
    const open = usage.data?.openOrders ?? 0;

    if (status === "Inactive") {
        return (
            <div className="space-y-2">
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={() => setStatus("Active")}
                >
                    Reactivate {noun}
                </Button>
                {save.isError && (
                    <p role="alert" className="text-sm text-late">
                        {save.error.message}
                    </p>
                )}
            </div>
        );
    }

    if (!confirming) {
        return (
            <Button
                variant="outline"
                tone="danger"
                className="h-11"
                onClick={() => {
                    save.reset();
                    setConfirming(true);
                }}
            >
                Deactivate {noun}
            </Button>
        );
    }

    return (
        <div className="w-full space-y-3 rounded-sm border border-late/40 bg-card p-3">
            {usage.isLoading && <Skeleton className="h-10 w-full" />}
            {usage.isError && (
                <p role="alert" className="text-sm text-late">
                    {usage.error.message}
                </p>
            )}
            {usage.data &&
                (open > 0 ? (
                    <p className="text-sm">
                        <span className="font-semibold text-late">
                            Still in use: {openOrdersText(open)}.
                        </span>{" "}
                        Those orders keep the {noun}, but it can no longer be
                        picked for new orders.
                    </p>
                ) : (
                    <p className="text-sm">
                        No open orders use this {noun}. It can no longer be
                        picked for new orders.
                    </p>
                ))}
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    tone="danger"
                    className="h-11"
                    disabled={!usage.data || save.isPending}
                    onClick={() => setStatus("Inactive")}
                >
                    {save.isPending
                        ? "Deactivating…"
                        : open > 0
                          ? "Deactivate anyway"
                          : `Deactivate ${noun}`}
                </Button>
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={() => setConfirming(false)}
                >
                    Keep it active
                </Button>
            </div>
        </div>
    );
}
