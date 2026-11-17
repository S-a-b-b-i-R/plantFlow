import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Skeleton } from "@/components/ui/skeleton";
import { PourCard } from "@/components/operator/PourCard";
import { ReturnsPanel } from "@/components/operator/ReturnsPanel";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { api } from "@/lib/api";
import { cft, clockSeconds, plantTime } from "@/lib/format";
import { loadState, utcMs } from "@/lib/operator";
import type {
    LiveDashboard,
    ReturnedConcrete,
    ShiftProduction,
    TruckOption,
} from "@/lib/types";

export function Operator() {
    const me = useMe().data!;
    const [chosen, setChosen] = useState<string | null>(null);

    const board = useQuery({
        queryKey: ["live", ""],
        queryFn: () => api<LiveDashboard>("/api/dashboards/live"),
        refetchInterval: 15_000,
    });

    const plans = (board.data?.plans ?? []).filter(
        (p) => p.status === "Approved",
    );
    const plan = plans.find((p) => p.planId === chosen) ?? plans[0];
    const planId = plan?.planId;

    const production = useQuery({
        queryKey: ["operator", planId],
        queryFn: () =>
            api<ShiftProduction>(`/api/shift-plans/${planId}/operator`),
        enabled: !!planId,
        refetchInterval: 10_000,
    });

    const trucks = useQuery({
        queryKey: ["trucks", planId],
        queryFn: () => api<TruckOption[]>(`/api/shift-plans/${planId}/trucks`),
        enabled: !!planId,
        refetchInterval: 10_000,
    });

    const returns = useQuery({
        queryKey: ["returns", planId],
        queryFn: () =>
            api<ReturnedConcrete[]>(`/api/returns?shiftPlanId=${planId}`),
        enabled: !!planId,
        refetchInterval: 10_000,
    });
    const stock = returns.data ?? [];

    // The server's clock, refreshed with the board. Reading the browser clock while rendering is not allowed.
    const now = board.data ? utcMs(board.data.asOfUtc) : 0;
    const cards = (production.data?.pours ?? [])
        .map((prod) => ({
            prod,
            live: plan?.pours.find((p) => p.pourId === prod.pourId),
        }))
        .flatMap((entry) =>
            entry.live ? [{ ...entry, live: entry.live }] : [],
        )
        .sort(
            (a, b) =>
                Number(b.live.late) - Number(a.live.late) ||
                a.live.plannedStart.localeCompare(b.live.plannedStart),
        );

    const waiting = cards
        .flatMap((c) => c.prod.loads)
        .filter((l) => loadState(l) === "ready").length;
    const inspect = stock.filter(
        (r) => r.status === "AwaitingInspection",
    ).length;
    const next = cards.find((c) => c.prod.remainingCft > 0);

    const [headline, tone] = waiting
        ? [
              waiting === 1
                  ? "1 load is ready to leave"
                  : `${waiting} loads are ready to leave`,
              "text-foreground",
          ]
        : inspect
          ? [
                inspect === 1
                    ? "1 returned load needs inspection"
                    : `${inspect} returned loads need inspection`,
                "text-foreground",
            ]
          : next
            ? [
                  `Next to make: ${next.live.customerName}, ${plantTime(next.live.plannedStart)}`,
                  next.live.late ? "text-late" : "text-foreground",
              ]
            : cards.length
              ? ["Everything is produced", "text-ok"]
              : ["Nothing to make", "text-muted-foreground"];

    return (
        <div className="space-y-6">
            <div>
                <h1
                    className={`display text-4xl leading-[0.95] md:text-6xl ${tone}`}
                >
                    {headline}
                </h1>
                {production.data && (
                    <p className="mt-3 text-lg text-muted-foreground">
                        {cft(production.data.producedCft)} of{" "}
                        {cft(production.data.plannedCft)} CFT made this shift,{" "}
                        {cft(production.data.remainingCft)} to go.
                        {board.data
                            ? ` Updated ${clockSeconds(board.data.asOfUtc)}.`
                            : ""}
                    </p>
                )}
            </div>

            {plans.length > 1 && (
                <div
                    className="flex flex-wrap gap-2"
                    role="group"
                    aria-label="Shift"
                >
                    {plans.map((p) => (
                        <button
                            key={p.planId}
                            type="button"
                            aria-pressed={p.planId === planId}
                            onClick={() => setChosen(p.planId)}
                            className={`h-11 rounded-md border px-4 text-sm font-medium ${
                                p.planId === planId
                                    ? "border-ink bg-ink text-white"
                                    : "bg-card"
                            }`}
                        >
                            {p.plantName}, {p.shiftName}
                        </button>
                    ))}
                </div>
            )}

            {(board.isPending || (!!planId && production.isPending)) && (
                <div className="space-y-4">
                    <Skeleton className="h-56 w-full" />
                    <Skeleton className="h-56 w-full" />
                </div>
            )}

            {(board.isError || production.isError) && (
                <div className="rounded-sm border bg-card p-5">
                    <p className="text-sm">
                        Could not load the console.{" "}
                        {(board.error ?? production.error)?.message}
                    </p>
                    <Button
                        className="mt-3"
                        size="sm"
                        onClick={() => {
                            void board.refetch();
                            void production.refetch();
                        }}
                    >
                        Try again
                    </Button>
                </div>
            )}

            {board.data && plans.length === 0 && (
                <div className="rounded-sm border bg-card p-8">
                    <p className="display text-3xl">No approved shift plan</p>
                    <p className="mt-2 max-w-md text-muted-foreground">
                        Loads can be made once the plant manager approves the
                        shift plan. It will show up here by itself.
                    </p>
                </div>
            )}

            <ReturnsPanel
                stock={stock}
                canDecide={can(me, PERMISSIONS.returnsQc)}
            />

            {cards.map(({ prod, live }) => (
                <PourCard
                    key={prod.pourId}
                    live={live}
                    production={prod}
                    trucks={trucks.data ?? []}
                    stock={stock}
                    now={now}
                    canTrack={can(me, PERMISSIONS.deliveriesTrack)}
                    canReturns={can(me, PERMISSIONS.returnsManage)}
                    canOverride={can(me, PERMISSIONS.deliveriesPlanOverride)}
                    returnWindow={production.data?.returnWindowMinutes ?? 120}
                />
            ))}
        </div>
    );
}
