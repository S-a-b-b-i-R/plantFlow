import { useState } from "react";
import { LuCheck } from "react-icons/lu";
import { Button } from "@/components/ui/button";
import { cft, plantTime } from "@/lib/format";
import {
    isOut,
    loadAction,
    loadState,
    minutesSince,
    pumpAction,
    stateLabel,
    stateSince,
} from "@/lib/operator";
import { stepRows } from "@/lib/timeline";
import type {
    LivePour,
    OperatorLoad,
    OperatorPour,
    ReturnedConcrete,
    TruckOption,
} from "@/lib/types";
import { LoadIssue, type IssueKind } from "./LoadIssue";
import { MakeLoad } from "./MakeLoad";
import { useAct } from "./useAct";

const primary =
    "h-12 bg-signal px-5 text-base font-semibold text-ink hover:bg-signal/90";

export function PourCard({
    live,
    production,
    trucks,
    stock,
    now,
    canTrack,
    canReturns,
    canOverride,
}: {
    live: LivePour;
    production: OperatorPour;
    trucks: TruckOption[];
    stock: ReturnedConcrete[];
    now: number;
    canTrack: boolean;
    canReturns: boolean;
    canOverride: boolean;
}) {
    const act = useAct();
    const truckName = (id: string) =>
        trucks.find((t) => t.id === id)?.code ?? "Truck";

    const loads = production.loads
        .filter((l) => loadState(l) !== "cancelled")
        .sort((a, b) => a.number - b.number);

    const overdue = stepRows(live).find(
        (row) => row.step.actualUtc === null && row.step.delayMinutes !== null,
    );

    const pumpNext = production.pumpId ? pumpAction(production) : null;
    const pumpSteps: [string, string | null][] = [
        ["Pump left", production.pumpDispatchedAtUtc],
        ["Pump arrived", production.pumpArrivedAtUtc],
        ["Pump ready", production.pumpAssembledAtUtc],
    ];

    const run = (action: { url: string; body: Record<string, unknown> }) =>
        act.mutate(action);

    return (
        <article
            className={`rounded-sm border border-l-4 bg-card ${live.late ? "border-l-late" : "border-l-transparent"}`}
        >
            <header className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2 border-b px-4 py-4 sm:px-5">
                <div className="min-w-0">
                    <div className="flex flex-wrap items-baseline gap-x-3">
                        <p className="display text-4xl leading-none tabular-nums">
                            {plantTime(live.plannedStart)}
                        </p>
                        {live.late && (
                            <p className="text-sm font-semibold text-late">
                                Running late
                            </p>
                        )}
                    </div>
                    <p className="mt-1.5 text-lg font-medium leading-tight">
                        {live.customerName}
                    </p>
                    <p className="text-sm text-muted-foreground">
                        {live.siteName}
                        {production.pumpId ? ", pumped" : ", no pump"}
                    </p>
                    {overdue && (
                        <p className="mt-2 text-sm font-semibold text-late">
                            {overdue.label} is {overdue.step.delayMinutes} min
                            overdue
                        </p>
                    )}
                </div>
                <div className="text-right">
                    <p className="display text-3xl leading-none tabular-nums">
                        {cft(production.producedCft)}
                        <span className="text-xl text-muted-foreground">
                            {" "}
                            / {cft(production.plannedCft)} CFT
                        </span>
                    </p>
                    <p className="mt-1.5 text-sm text-muted-foreground">
                        {production.remainingCft > 0
                            ? `${cft(production.remainingCft)} CFT left to make`
                            : "Fully produced"}
                    </p>
                </div>
            </header>

            {production.pumpId && (
                <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3 sm:px-5">
                    {pumpSteps.map(([label, at]) =>
                        at ? (
                            <span
                                key={label}
                                className="inline-flex h-9 items-center gap-1.5 rounded-full bg-ok/10 px-3 text-sm font-medium text-ok"
                            >
                                <LuCheck
                                    className="size-4"
                                    aria-hidden="true"
                                />
                                {label}
                            </span>
                        ) : pumpNext && pumpNext.label === label ? (
                            canTrack ? (
                                <Button
                                    key={label}
                                    className={primary}
                                    disabled={act.isPending}
                                    onClick={() => run(pumpNext)}
                                >
                                    {label}
                                </Button>
                            ) : (
                                <span
                                    key={label}
                                    className="inline-flex h-9 items-center rounded-full border px-3 text-sm"
                                >
                                    {label}
                                </span>
                            )
                        ) : (
                            <span
                                key={label}
                                className="inline-flex h-9 items-center rounded-full border border-dashed px-3 text-sm text-muted-foreground"
                            >
                                {label}
                            </span>
                        ),
                    )}
                </div>
            )}

            <div className="px-4 py-4 sm:px-5">
                {loads.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                        No loads made yet.
                    </p>
                ) : (
                    <ul className="divide-y">
                        {loads.map((load) => (
                            <LoadRow
                                key={load.id}
                                load={load}
                                truck={truckName(load.truckId)}
                                now={now}
                                canTrack={canTrack}
                                canReturns={canReturns}
                                busy={act.isPending}
                                pumpPending={
                                    !!production.pumpId &&
                                    !production.pumpAssembledAtUtc
                                }
                                onAct={run}
                            />
                        ))}
                    </ul>
                )}

                {act.isError && (
                    <p
                        className="mt-3 text-sm font-medium text-late"
                        role="alert"
                    >
                        {act.error.message}
                    </p>
                )}

                <div className="mt-4">
                    <MakeLoad
                        pourId={production.pourId}
                        productId={production.productId}
                        remaining={production.remainingCft}
                        trucks={trucks}
                        stock={stock}
                        canOverride={canOverride}
                    />
                </div>
            </div>
        </article>
    );
}

function issuesFor(
    load: OperatorLoad,
    canReturns: boolean,
): [IssueKind, string][] {
    switch (loadState(load)) {
        case "ready":
            return [
                ["cancel", "Cancel"],
                ...(canReturns
                    ? ([["rejectPlant", "Reject at plant"]] as [
                          IssueKind,
                          string,
                      ][])
                    : []),
            ];
        case "road":
            return [["cancel", "Cancel"]];
        case "site":
            return canReturns ? [["rejectSite", "Reject at site"]] : [];
        case "back":
        case "done":
            return canReturns && load.returnedCft === 0
                ? [["part", "Concrete came back"]]
                : [];
        default:
            return [];
    }
}

function LoadRow({
    load,
    truck,
    now,
    canTrack,
    canReturns,
    busy,
    pumpPending,
    onAct,
}: {
    load: OperatorLoad;
    truck: string;
    now: number;
    canTrack: boolean;
    canReturns: boolean;
    busy: boolean;
    pumpPending: boolean;
    onAct: (a: { url: string; body: Record<string, unknown> }) => void;
}) {
    const [issue, setIssue] = useState<IssueKind | null>(null);
    const state = loadState(load);
    const action = loadAction(load);
    const since = stateSince(load);
    const issues = issuesFor(load, canReturns);
    // Sending a load out is the operator's job. The later steps are recorded by whoever tracks trucks.
    const allowed = action && (state === "ready" || canTrack);
    // A truck at a pumped pour waits for the pump before it can start pouring.
    const held = pumpPending && state === "site";

    return (
        <li className="flex flex-wrap items-center gap-x-4 gap-y-2 py-3">
            <p className="display w-14 text-3xl leading-none tabular-nums">
                L{load.number}
            </p>
            <div className="min-w-0 flex-1 basis-48">
                <p className="font-medium">
                    {truck} <span className="text-muted-foreground">·</span>{" "}
                    <span className="tabular-nums">
                        {cft(load.quantityCft)}
                    </span>{" "}
                    CFT
                </p>
                <p
                    className={`text-sm ${isOut(load) ? "" : "text-muted-foreground"}`}
                >
                    {stateLabel[state]}
                    {since ? `, ${minutesSince(since, now)} min` : ""}
                    {load.quantityReason ? ` · ${load.quantityReason}` : ""}
                    {state === "rejected" && load.rejectReason
                        ? ` · ${load.rejectReason}`
                        : ""}
                    {load.returnedCft > 0
                        ? ` · ${cft(load.returnedCft)} CFT came back`
                        : ""}
                </p>
                {held && (
                    <p className="text-sm font-semibold text-late">
                        Waiting for the pump to be ready
                    </p>
                )}
            </div>
            {allowed && (
                <Button
                    className={primary}
                    disabled={busy || held}
                    onClick={() => onAct(action)}
                >
                    {action.label}
                </Button>
            )}

            {issues.length > 0 && issue === null && (
                <div className="flex basis-full flex-wrap gap-2 pl-0 sm:pl-[4.5rem]">
                    {issues.map(([kind, label]) => (
                        <Button
                            key={kind}
                            variant="outline"
                            className="h-11 px-4"
                            onClick={() => setIssue(kind)}
                        >
                            {label}
                        </Button>
                    ))}
                </div>
            )}

            {issue && (
                <LoadIssue
                    load={load}
                    kind={issue}
                    onClose={() => setIssue(null)}
                />
            )}
        </li>
    );
}
