import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import { cft, clock, plantTime } from "@/lib/format";
import type {
    LiveDashboard,
    LivePlan,
    LoadCounts,
    LivePour,
    StepDelay,
} from "@/lib/types";

const loadLabels: [keyof LoadCounts, string][] = [
    ["waiting", "Waiting"],
    ["onTheRoad", "On road"],
    ["atSite", "At site"],
    ["pouring", "Pouring"],
    ["returning", "Returning"],
    ["done", "Done"],
];

export function LiveBoard() {
    // Empty means today at the plants. The API works out the date.
    const [date, setDate] = useState("");

    const board = useQuery({
        queryKey: ["live", date],
        queryFn: () =>
            api<LiveDashboard>(
                `/api/dashboards/live${date ? `?date=${date}` : ""}`,
            ),
        refetchInterval: 15_000,
    });

    return (
        <div className="space-y-6">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-semibold tracking-tight">
                        Live operations
                    </h1>
                    <p className="text-sm text-muted-foreground">
                        {board.data
                            ? `${board.data.date} · updated ${clock(board.data.asOfUtc)} · a step is late after ${board.data.lateThresholdMinutes} min`
                            : "Shifts, loads and delays against plan."}
                    </p>
                </div>

                <div className="flex items-center gap-2">
                    <input
                        type="date"
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                        className="h-9 rounded-md border bg-background px-3 text-sm"
                        aria-label="Date"
                    />
                    <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setDate("")}
                        disabled={!date}
                    >
                        Today
                    </Button>
                </div>
            </div>

            {board.isPending && <Skeleton className="h-64 w-full" />}

            {board.isError && (
                <Card>
                    <CardContent className="space-y-3 pt-6">
                        <p className="text-sm">
                            Could not load the board: {board.error.message}
                        </p>
                        <Button size="sm" onClick={() => void board.refetch()}>
                            Try again
                        </Button>
                    </CardContent>
                </Card>
            )}

            {board.data && (
                <>
                    <Summary board={board.data} />

                    {board.data.plans.length === 0 ? (
                        <Card>
                            <CardContent className="pt-6 text-sm text-muted-foreground">
                                No shift plans for this date.
                            </CardContent>
                        </Card>
                    ) : (
                        board.data.plans.map((plan) => (
                            <PlanCard key={plan.planId} plan={plan} />
                        ))
                    )}
                </>
            )}
        </div>
    );
}

function Summary({ board }: { board: LiveDashboard }) {
    const tiles = [
        { label: "Planned", value: `${cft(board.plannedCft)} CFT` },
        { label: "Produced", value: `${cft(board.producedCft)} CFT` },
        { label: "Delivered", value: `${cft(board.deliveredCft)} CFT` },
        {
            label: "Late pours",
            value: String(board.latePours),
            alert: board.latePours > 0,
        },
        { label: "On the road", value: String(board.loads.onTheRoad) },
        {
            label: "At site / pouring",
            value: `${board.loads.atSite} / ${board.loads.pouring}`,
        },
    ];

    return (
        <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
            {tiles.map((tile) => (
                <Card key={tile.label}>
                    <CardContent className="space-y-1 pt-4">
                        <p className="text-xs text-muted-foreground">
                            {tile.label}
                        </p>
                        <p
                            className={`text-xl font-semibold tabular-nums ${tile.alert ? "text-destructive" : ""}`}
                        >
                            {tile.value}
                        </p>
                    </CardContent>
                </Card>
            ))}
        </div>
    );
}

function PlanCard({ plan }: { plan: LivePlan }) {
    const approved = plan.status === "Approved";
    const percent =
        plan.plannedCft > 0
            ? Math.min(100, (plan.producedCft / plan.plannedCft) * 100)
            : 0;

    return (
        <Card>
            <CardHeader className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <CardTitle className="text-lg">
                        {plan.plantName}{" "}
                        <span className="font-normal text-muted-foreground">
                            · {plan.shiftName}
                        </span>
                    </CardTitle>
                    <div className="flex items-center gap-2">
                        {plan.latePours > 0 && (
                            <Badge variant="destructive">
                                {plan.latePours} late
                            </Badge>
                        )}
                        <Badge variant={approved ? "default" : "secondary"}>
                            {approved ? "Approved" : plan.status}
                        </Badge>
                    </div>
                </div>

                <div className="space-y-1">
                    <div className="flex justify-between text-xs text-muted-foreground">
                        <span>
                            Produced {cft(plan.producedCft)} of{" "}
                            {cft(plan.plannedCft)} CFT ·{" "}
                            {cft(plan.remainingCft)} left
                        </span>
                        <span>Delivered {cft(plan.deliveredCft)} CFT</span>
                    </div>
                    <div className="h-2 rounded-full bg-muted">
                        <div
                            className="h-2 rounded-full bg-primary"
                            style={{ width: `${percent}%` }}
                        />
                    </div>
                    <Loads counts={plan.loads} />
                </div>

                {!approved && (
                    <p className="text-xs text-muted-foreground">
                        Delays show once the plant manager approves this plan.
                    </p>
                )}
            </CardHeader>

            <CardContent className="overflow-x-auto">
                <table className="w-full min-w-[820px] text-left text-sm">
                    <thead className="text-xs text-muted-foreground">
                        <tr className="border-b">
                            <th className="py-2 pr-3 font-medium">Start</th>
                            <th className="py-2 pr-3 font-medium">
                                Customer · site
                            </th>
                            <th className="py-2 pr-3 font-medium">Concrete</th>
                            <th className="py-2 pr-3 font-medium">Loads</th>
                            <th className="py-2 pr-3 font-medium">
                                Pump leaves
                            </th>
                            <th className="py-2 pr-3 font-medium">
                                Pump ready
                            </th>
                            <th className="py-2 pr-3 font-medium">
                                First truck
                            </th>
                            <th className="py-2 font-medium">Pour starts</th>
                        </tr>
                    </thead>
                    <tbody>
                        {plan.pours.map((pour) => (
                            <PourRow key={pour.pourId} pour={pour} />
                        ))}
                    </tbody>
                </table>
            </CardContent>
        </Card>
    );
}

function PourRow({ pour }: { pour: LivePour }) {
    return (
        <tr
            className={`border-b align-top last:border-0 ${pour.late ? "bg-destructive/5" : ""}`}
        >
            <td className="py-3 pr-3 font-medium tabular-nums">
                {plantTime(pour.plannedStart)}
            </td>
            <td className="py-3 pr-3">
                <div>{pour.customerName}</div>
                <div className="text-xs text-muted-foreground">
                    {pour.siteName} · {pour.pumpId ? "pumped" : "no pump"}
                </div>
            </td>
            <td className="py-3 pr-3 tabular-nums">
                {cft(pour.producedCft)} / {cft(pour.plannedCft)}
                <div className="text-xs text-muted-foreground">
                    delivered {cft(pour.deliveredCft)}
                </div>
            </td>
            <td className="py-3 pr-3">
                <Loads counts={pour.loads} />
            </td>
            <td className="py-3 pr-3">
                <Step step={pour.pumpDispatch} />
            </td>
            <td className="py-3 pr-3">
                <Step step={pour.pumpReady} />
            </td>
            <td className="py-3 pr-3">
                <Step step={pour.firstTruck} />
            </td>
            <td className="py-3">
                <Step step={pour.pourStart} />
            </td>
        </tr>
    );
}

function Loads({ counts }: { counts: LoadCounts }) {
    return (
        <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs">
            {loadLabels.map(([key, label]) => (
                <span
                    key={key}
                    className={
                        counts[key] ? "font-medium" : "text-muted-foreground"
                    }
                >
                    {label} <span className="tabular-nums">{counts[key]}</span>
                </span>
            ))}
        </div>
    );
}

// One step against its plan: not due yet, overdue, or done and how far off.
function Step({ step }: { step: StepDelay | null }) {
    if (!step) return <span className="text-muted-foreground">—</span>;

    if (step.delayMinutes === null) {
        return (
            <span className="text-muted-foreground">
                due {clock(step.plannedUtc)}
            </span>
        );
    }

    const text =
        step.delayMinutes > 0
            ? `+${step.delayMinutes} min`
            : step.delayMinutes < 0
              ? `${step.delayMinutes} min`
              : "on time";

    return (
        <div className="flex flex-col gap-0.5">
            <span className="tabular-nums">
                {step.actualUtc ? (
                    clock(step.actualUtc)
                ) : (
                    <span className="text-muted-foreground">not yet</span>
                )}
            </span>
            <Badge
                variant={step.late ? "destructive" : "secondary"}
                className="w-fit"
            >
                {step.actualUtc ? text : `overdue ${text}`}
            </Badge>
        </div>
    );
}
