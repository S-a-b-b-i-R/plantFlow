import { useState } from "react";
import { LuPencil, LuTrash2 } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { ReasonAction } from "@/components/orders/ReasonAction";
import { nextButtonClass, panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cft, plantTime } from "@/lib/format";
import { methodLabel } from "@/lib/orders";
import {
    hhmm,
    planStatusLabel,
    type Pour,
    type PlantShift,
    type ShiftPlanStatus,
    type ShiftPlanSummary,
} from "@/lib/planning";
import { cn } from "@/lib/utils";
import { usePlan, usePlanAct } from "./usePlanning";

const tones: Record<ShiftPlanStatus, string> = {
    Draft: "border-border bg-muted text-muted-foreground",
    Submitted: "border-ink/30 bg-ink/5 text-foreground",
    Approved: "border-ok/40 bg-ok/10 text-ok",
};

function PlanChip({ status }: { status: ShiftPlanStatus }) {
    return (
        <span
            className={cn(
                "inline-flex w-fit items-center rounded-sm border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
                tones[status],
            )}
        >
            {planStatusLabel[status]}
        </span>
    );
}

const shiftWindow = (shift: PlantShift) =>
    `${hhmm(shift.startTime)}–${hhmm(shift.endTime)}${shift.crossesMidnight ? " (next day)" : ""}`;

// One shift of the day: its plan, its pours, and what the person can do with the plan next.
export function ShiftSection({
    shift,
    summary,
    plantId,
    date,
    onEditPour,
}: {
    shift: PlantShift;
    summary: ShiftPlanSummary | undefined;
    plantId: string;
    date: string;
    onEditPour: (pour: Pour, summary: ShiftPlanSummary) => void;
}) {
    const me = useMe().data!;
    const act = usePlanAct();
    const [error, setError] = useState<string | null>(null);

    const canPlan = can(me, PERMISSIONS.deliveriesPlan);
    const canDecide = can(me, PERMISSIONS.deliveriesPlanApprove);

    const run = (call: Parameters<typeof act.mutate>[0]) => {
        setError(null);
        act.mutate(call, { onError: (e) => setError(e.message) });
    };

    return (
        <section className={cn(panelClass, "p-4")} aria-label={shift.name}>
            <header className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    <h2 className="display text-2xl leading-none">
                        {shift.name}
                    </h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                        {shiftWindow(shift)}
                        {summary && summary.pourCount > 0 && (
                            <>
                                {" · "}
                                {summary.pourCount}{" "}
                                {summary.pourCount === 1 ? "pour" : "pours"},{" "}
                                {cft(summary.totalPlannedCft)} CFT
                            </>
                        )}
                    </p>
                </div>
                {summary ? (
                    <PlanChip status={summary.status} />
                ) : (
                    <span className="text-sm text-muted-foreground">
                        No plan yet
                    </span>
                )}
            </header>

            {summary?.status === "Draft" && summary.returnReason && (
                <p className="mt-3 rounded-sm border border-late/40 bg-late/10 px-3 py-2 text-sm text-late">
                    Sent back: {summary.returnReason}
                </p>
            )}

            {summary ? (
                <PlanBody
                    summary={summary}
                    canEdit={canPlan && summary.status === "Draft"}
                    onEditPour={onEditPour}
                    onRemove={(pour) =>
                        run({ url: `/api/pours/${pour.id}`, method: "DELETE" })
                    }
                    removing={act.isPending}
                />
            ) : (
                <p className="mt-3 text-sm text-muted-foreground">
                    {canPlan
                        ? "Nothing is planned for this shift. Plan a pour from the list of orders, or start the plan here."
                        : "Nothing is planned for this shift."}
                </p>
            )}

            {error && (
                <p role="alert" className="mt-3 text-sm text-late">
                    {error}
                </p>
            )}

            <div className="mt-4 flex flex-wrap gap-2">
                {!summary && canPlan && (
                    <Button
                        variant="outline"
                        className="h-10 px-4"
                        disabled={act.isPending}
                        onClick={() =>
                            run({
                                url: "/api/shift-plans",
                                body: { plantId, date, shiftId: shift.id },
                            })
                        }
                    >
                        Start plan
                    </Button>
                )}

                {summary?.status === "Draft" && canPlan && (
                    <Button
                        className={cn(
                            "h-10 px-5",
                            summary.pourCount > 0 && nextButtonClass,
                        )}
                        disabled={act.isPending || summary.pourCount === 0}
                        onClick={() =>
                            run({
                                url: `/api/shift-plans/${summary.id}/submit`,
                            })
                        }
                    >
                        Submit for approval
                    </Button>
                )}

                {summary?.status === "Submitted" && canDecide && (
                    <>
                        <Button
                            className={cn("h-10 px-5", nextButtonClass)}
                            disabled={act.isPending}
                            onClick={() =>
                                run({
                                    url: `/api/shift-plans/${summary.id}/approve`,
                                })
                            }
                        >
                            Approve plan
                        </Button>
                        <ReasonAction
                            label="Send back"
                            question="What should the planners change?"
                            submitLabel="Send back"
                            pending={act.isPending}
                            onSubmit={(reason) =>
                                run({
                                    url: `/api/shift-plans/${summary.id}/reject`,
                                    body: { reason },
                                })
                            }
                        />
                    </>
                )}

                {summary?.status === "Approved" && canDecide && (
                    <ReasonAction
                        label="Reopen plan"
                        question="Why does the plan need to change?"
                        submitLabel="Reopen"
                        pending={act.isPending}
                        onSubmit={(reason) =>
                            run({
                                url: `/api/shift-plans/${summary.id}/reopen`,
                                body: { reason },
                            })
                        }
                    />
                )}
            </div>
        </section>
    );
}

function PlanBody({
    summary,
    canEdit,
    onEditPour,
    onRemove,
    removing,
}: {
    summary: ShiftPlanSummary;
    canEdit: boolean;
    onEditPour: (pour: Pour, summary: ShiftPlanSummary) => void;
    onRemove: (pour: Pour) => void;
    removing: boolean;
}) {
    const plan = usePlan(summary.id);

    if (plan.isLoading) return <Skeleton className="mt-3 h-20 w-full" />;
    if (plan.isError || !plan.data) {
        return (
            <p role="alert" className="mt-3 text-sm text-late">
                The pours could not be loaded.
            </p>
        );
    }

    if (plan.data.pours.length === 0) {
        return (
            <p className="mt-3 text-sm text-muted-foreground">
                No pours yet. Add one from the list of orders to plan.
            </p>
        );
    }

    return (
        <ul className="mt-3 divide-y border-y">
            {plan.data.pours.map((pour) => (
                <li
                    key={pour.id}
                    className="flex flex-wrap items-start justify-between gap-3 py-3"
                >
                    <div className="min-w-0 text-sm">
                        <p className="font-semibold">
                            <span className="display mr-2 text-lg">
                                {plantTime(pour.plannedStart)}
                            </span>
                            {pour.orderNumber} · {pour.customerName}
                        </p>
                        <p className="text-muted-foreground">
                            {pour.siteName} · {pour.productCode} ·{" "}
                            {cft(pour.plannedCft)} CFT ·{" "}
                            {methodLabel[pour.pourMethod]}
                        </p>
                        <p className="text-muted-foreground">
                            {pour.pumpCode && (
                                <>
                                    Pump {pour.pumpCode}
                                    {pour.operatorName
                                        ? ` (${pour.operatorName})`
                                        : ""}
                                    {" · leaves "}
                                    {pour.pumpLeaveAt
                                        ? plantTime(pour.pumpLeaveAt)
                                        : "—"}
                                    {" · "}
                                </>
                            )}
                            First truck leaves{" "}
                            {plantTime(pour.firstTruckLeaveAt)} · about{" "}
                            {pour.estimatedTruckLoads}{" "}
                            {pour.estimatedTruckLoads === 1 ? "load" : "loads"}
                        </p>
                        {pour.crewNotes && (
                            <p className="text-muted-foreground">
                                Crew: {pour.crewNotes}
                            </p>
                        )}
                        {pour.restOverrideReason && (
                            <p className="text-late">
                                Pump rest overridden: {pour.restOverrideReason}
                            </p>
                        )}
                    </div>
                    {canEdit && (
                        <div className="flex gap-2">
                            <Button
                                variant="outline"
                                className="h-9 px-3"
                                aria-label={`Edit the ${plantTime(pour.plannedStart)} pour`}
                                onClick={() => onEditPour(pour, summary)}
                            >
                                <LuPencil aria-hidden className="size-4" />
                                Edit
                            </Button>
                            <Button
                                variant="outline"
                                tone="danger"
                                className="h-9 px-3"
                                disabled={removing}
                                aria-label={`Remove the ${plantTime(pour.plannedStart)} pour`}
                                onClick={() => onRemove(pour)}
                            >
                                <LuTrash2 aria-hidden className="size-4" />
                                Remove
                            </Button>
                        </div>
                    )}
                </li>
            ))}
        </ul>
    );
}
