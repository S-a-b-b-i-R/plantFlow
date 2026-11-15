import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { PlanPanel } from "@/components/live/PlanPanel";
import { api } from "@/lib/api";
import { cft, clockSeconds, dayTitle, loadParts } from "@/lib/format";
import type { LiveDashboard } from "@/lib/types";

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

    const data = board.data;

    return (
        <div className="space-y-6">
            <div className="flex flex-wrap items-end justify-between gap-4">
                {data ? <Headline board={data} /> : <div className="h-20" />}

                <div className="flex items-center gap-2">
                    <input
                        type="date"
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                        className="h-9 rounded-md border bg-card px-3 text-sm"
                        aria-label="Show shifts for"
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

            {data && (
                <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2">
                    <Legend threshold={data.lateThresholdMinutes} />
                    <p
                        className="flex items-center gap-2 text-sm text-muted-foreground"
                        aria-live="polite"
                    >
                        <span
                            className={`size-2 rounded-full ${board.isFetching ? "bg-signal" : "bg-ok"}`}
                            aria-hidden="true"
                        />
                        {dayTitle(data.date)}, updated{" "}
                        {clockSeconds(data.asOfUtc)}
                    </p>
                </div>
            )}

            {board.isPending && (
                <div className="space-y-4">
                    <Skeleton className="h-72 w-full" />
                    <Skeleton className="h-72 w-full" />
                </div>
            )}

            {board.isError && (
                <div className="rounded-sm border bg-card p-5">
                    <p className="text-sm">
                        Could not load the board. {board.error.message}
                    </p>
                    <Button
                        className="mt-3"
                        size="sm"
                        onClick={() => void board.refetch()}
                    >
                        Try again
                    </Button>
                </div>
            )}

            {data && data.plans.length === 0 && (
                <div className="rounded-sm border bg-card p-8">
                    <p className="display text-3xl">No shifts planned</p>
                    <p className="mt-2 max-w-md text-muted-foreground">
                        Shifts for {dayTitle(data.date)} appear here once a
                        plant manager creates a plan. Pick another date to look
                        at a different day.
                    </p>
                </div>
            )}

            {data &&
                data.plans.map((plan) => (
                    <PlanPanel
                        key={plan.planId}
                        plan={plan}
                        now={Date.parse(data.asOfUtc)}
                    />
                ))}
        </div>
    );
}

function Headline({ board }: { board: LiveDashboard }) {
    const pours = board.plans.reduce(
        (count, plan) => count + plan.pours.length,
        0,
    );

    const [text, tone] =
        pours === 0
            ? ["Nothing planned", "text-muted-foreground"]
            : board.latePours > 0
              ? [
                    board.latePours === 1
                        ? "1 pour is running late"
                        : `${board.latePours} pours are running late`,
                    "text-late",
                ]
              : ["All pours are on plan", "text-foreground"];

    const trucks = loadParts(board.loads);

    return (
        <div>
            <h1
                className={`display text-5xl leading-[0.95] md:text-7xl ${tone}`}
            >
                {text}
            </h1>
            <p className="mt-3 max-w-2xl text-lg text-muted-foreground">
                {cft(board.producedCft)} of {cft(board.plannedCft)} CFT
                produced, {cft(board.deliveredCft)} delivered.{" "}
                {trucks.length
                    ? `Trucks: ${trucks.join(", ")}.`
                    : "No trucks out."}
            </p>
        </div>
    );
}

function Legend({ threshold }: { threshold: number }) {
    return (
        <ul className="flex flex-wrap items-center gap-x-5 gap-y-1.5 text-sm text-muted-foreground">
            <li className="flex items-center gap-2">
                <span
                    className="size-3 rounded-full border-2 border-muted-foreground bg-card"
                    aria-hidden="true"
                />
                Planned time
            </li>
            <li className="flex items-center gap-2">
                <span
                    className="size-3 rounded-full bg-ok"
                    aria-hidden="true"
                />
                Done on time
            </li>
            <li className="flex items-center gap-2">
                <span
                    className="size-3 rounded-full bg-late"
                    aria-hidden="true"
                />
                Done late
            </li>
            <li className="flex items-center gap-2">
                <span
                    className="h-0.5 w-5"
                    style={{
                        backgroundImage:
                            "repeating-linear-gradient(90deg, var(--late) 0 4px, transparent 4px 7px)",
                    }}
                    aria-hidden="true"
                />
                Overdue, still waiting
            </li>
            <li className="flex items-center gap-2">
                <span className="h-4 w-0.5 bg-signal" aria-hidden="true" />
                Now
            </li>
            <li>Late means more than {threshold} minutes behind plan</li>
        </ul>
    );
}
