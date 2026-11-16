import { useState } from "react";
import { Button } from "@/components/ui/button";
import { cft } from "@/lib/format";
import { faultLabel, kindLabel, returnStatusLabel } from "@/lib/operator";
import type { ReturnedConcrete } from "@/lib/types";
import { useAct } from "./useAct";

const inputClass =
    "h-12 w-full rounded-md border bg-card px-3 text-base placeholder:text-muted-foreground";

const settledStatuses = ["Used", "Dumped"];

export function ReturnsPanel({
    stock,
    canManage,
}: {
    stock: ReturnedConcrete[];
    canManage: boolean;
}) {
    if (stock.length === 0) return null;

    const open = stock.filter((r) => !settledStatuses.includes(r.status));
    const settled = stock.filter((r) => settledStatuses.includes(r.status));

    return (
        <section className="rounded-sm border bg-card">
            <header className="border-b px-4 py-4 sm:px-5">
                <h2 className="display text-3xl leading-none">
                    Returned concrete
                </h2>
                <p className="mt-1.5 text-sm text-muted-foreground">
                    {open.length
                        ? "Inspect it, then reuse, treat or dump it. Good concrete can go into a new load of the same product."
                        : "Nothing is waiting for a decision."}
                </p>
            </header>

            {open.length > 0 && (
                <ul className="divide-y">
                    {open.map((item) => (
                        <ReturnRow
                            key={item.id}
                            item={item}
                            canManage={canManage}
                        />
                    ))}
                </ul>
            )}

            {settled.length > 0 && (
                <details className="border-t px-4 py-3 sm:px-5">
                    <summary className="cursor-pointer text-sm text-muted-foreground">
                        Settled ({settled.length})
                    </summary>
                    <ul className="mt-2 divide-y">
                        {settled.map((item) => (
                            <li
                                key={item.id}
                                className="py-2 text-sm text-muted-foreground"
                            >
                                {item.productCode ?? "Concrete"},{" "}
                                {cft(item.quantityCft)} CFT,{" "}
                                {kindLabel[item.kind].toLowerCase()},{" "}
                                {returnStatusLabel[item.status].toLowerCase()}
                                {item.dumpedCft > 0
                                    ? ` (${cft(item.dumpedCft)} CFT dumped)`
                                    : ""}
                            </li>
                        ))}
                    </ul>
                </details>
            )}
        </section>
    );
}

function ReturnRow({
    item,
    canManage,
}: {
    item: ReturnedConcrete;
    canManage: boolean;
}) {
    const act = useAct();
    const [dumping, setDumping] = useState(false);
    const [note, setNote] = useState("");

    const decide = (decision: string, withNote?: string) =>
        act.mutate(
            {
                url: `/api/returns/${item.id}/decide`,
                body: { decision, note: withNote },
            },
            { onSuccess: () => setDumping(false) },
        );

    const choices: [string, string][] =
        item.status === "AwaitingInspection"
            ? [
                  ["Reusable", "Good to use"],
                  ["NeedsTreatment", "Needs treatment"],
              ]
            : item.status === "NeedsTreatment"
              ? [["Treated", "Treated"]]
              : [];
    const canDump = item.remainingCft > 0;

    return (
        <li className="px-4 py-4 sm:px-5">
            <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
                <div className="min-w-0">
                    <p className="font-medium">
                        {item.productCode ?? "Concrete"},{" "}
                        <span className="tabular-nums">
                            {cft(item.remainingCft)}
                        </span>{" "}
                        CFT
                    </p>
                    <p className="text-sm text-muted-foreground">
                        {kindLabel[item.kind]}, {faultLabel[item.fault]}:{" "}
                        {item.reason}
                    </p>
                    <p className="mt-1 text-sm font-semibold">
                        {returnStatusLabel[item.status]}
                    </p>
                </div>

                {canManage && !dumping && (
                    <div className="flex flex-wrap gap-2">
                        {choices.map(([decision, label]) => (
                            <Button
                                key={decision}
                                className="h-12 bg-signal px-5 text-base font-semibold text-ink hover:bg-signal/90"
                                disabled={act.isPending}
                                onClick={() => decide(decision)}
                            >
                                {label}
                            </Button>
                        ))}
                        {canDump && (
                            <Button
                                variant="outline"
                                className="h-12 px-5 text-base"
                                disabled={act.isPending}
                                onClick={() => setDumping(true)}
                            >
                                Dump
                            </Button>
                        )}
                    </div>
                )}
            </div>

            {dumping && (
                <div className="mt-3 rounded-sm border bg-muted/40 p-4">
                    <label className="block text-sm">
                        <span className="mb-1 block text-muted-foreground">
                            Why is it being dumped?
                        </span>
                        <input
                            value={note}
                            onChange={(e) => setNote(e.target.value)}
                            className={inputClass}
                        />
                    </label>
                    <div className="mt-3 flex flex-wrap gap-2">
                        <Button
                            className="h-12 px-5 text-base"
                            disabled={note.trim().length < 3 || act.isPending}
                            onClick={() => decide("Dumped", note.trim())}
                        >
                            Dump {cft(item.remainingCft)} CFT
                        </Button>
                        <Button
                            variant="outline"
                            className="h-12 px-5 text-base"
                            onClick={() => setDumping(false)}
                        >
                            Never mind
                        </Button>
                    </div>
                </div>
            )}

            {act.isError && (
                <p className="mt-3 text-sm font-medium text-late" role="alert">
                    {act.error.message}
                </p>
            )}
        </li>
    );
}
