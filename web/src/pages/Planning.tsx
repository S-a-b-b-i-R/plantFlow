import { useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { LuChevronLeft, LuChevronRight } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import {
    PourForm,
    type PourTarget,
    type ShiftChoice,
} from "@/components/planning/PourForm";
import { ShiftSection } from "@/components/planning/ShiftSection";
import { ToPlanList } from "@/components/planning/ToPlanList";
import {
    usePlans,
    useShifts,
    useToPlan,
} from "@/components/planning/usePlanning";
import { usePlants } from "@/components/pricing/useBook";
import { Skeleton } from "@/components/ui/skeleton";
import { dayTitle } from "@/lib/format";
import {
    addDays,
    todayIso,
    type Pour,
    type ShiftPlanSummary,
} from "@/lib/planning";

type Editing = { target: PourTarget; pour?: Pour; shiftId?: string };

// The day board: pick a plant and a day, see each shift with its plan and pours, and put
// the concrete that is still unplanned onto a shift.
export function Planning() {
    const me = useMe().data!;
    const [params, setParams] = useSearchParams();
    const [today] = useState(todayIso);
    const date = params.get("date") ?? today;
    const plants = usePlants();
    const plantId = params.get("plant") ?? plants.data?.items[0]?.id;

    const shifts = useShifts(plantId);
    const plans = usePlans(plantId, date);
    const toPlan = useToPlan(plantId, date);
    const [editing, setEditing] = useState<Editing | null>(null);
    const formRef = useRef<HTMLDivElement>(null);

    const canPlan = can(me, PERMISSIONS.deliveriesPlan);

    const setView = (next: { plant?: string; date?: string }) => {
        setEditing(null);
        const merged = new URLSearchParams(params);
        if (next.plant) merged.set("plant", next.plant);
        if (next.date) merged.set("date", next.date);
        setParams(merged, { replace: true });
    };

    const open = (value: Editing) => {
        setEditing(value);
        // Wait for the form to be on the page, then bring it into view.
        requestAnimationFrame(() =>
            formRef.current?.scrollIntoView({
                behavior: "smooth",
                block: "start",
            }),
        );
    };

    const activeShifts = (shifts.data ?? []).filter(
        (s) => s.status === "Active",
    );
    const planOf = (shiftId: string) =>
        plans.data?.items.find((p) => p.shiftId === shiftId);

    const choices: ShiftChoice[] = activeShifts
        .filter((s) => !planOf(s.id) || planOf(s.id)!.status === "Draft")
        .map((s) => ({ shift: s, planId: planOf(s.id)?.id ?? null }));

    const editPour = (pour: Pour, summary: ShiftPlanSummary) => {
        const shift = activeShifts.find((s) => s.id === summary.shiftId);
        if (!shift) return;
        const left = toPlan.data?.find(
            (l) => l.orderLineId === pour.orderLineId,
        )?.remainingCft;
        open({
            pour,
            shiftId: shift.id,
            target: {
                orderLineId: pour.orderLineId,
                title: `${pour.orderNumber} · ${pour.customerName}`,
                detail: `${pour.siteName} · ${pour.productCode}`,
                method: pour.pourMethod,
                remaining: left === undefined ? null : left + pour.plannedCft,
            },
        });
    };

    const formChoices = editing?.shiftId
        ? choices.filter((c) => c.shift.id === editing.shiftId)
        : choices;

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Planning</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    Each shift has one plan. Add pours to a draft, submit it,
                    and the plant head approves it as a whole.
                </p>
            </header>

            <div className="flex flex-wrap items-end gap-3">
                <Field label="Plant" className="w-56">
                    <select
                        className={inputClass}
                        value={plantId ?? ""}
                        onChange={(e) => setView({ plant: e.target.value })}
                    >
                        {(plants.data?.items ?? []).map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.code} · {p.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <div className="flex items-end gap-2">
                    <Button
                        variant="outline"
                        className="size-10 p-0"
                        aria-label="Previous day"
                        onClick={() => setView({ date: addDays(date, -1) })}
                    >
                        <LuChevronLeft aria-hidden className="size-4" />
                    </Button>
                    <Field label="Day" className="w-44">
                        <input
                            type="date"
                            className={inputClass}
                            value={date}
                            onChange={(e) =>
                                e.target.value &&
                                setView({ date: e.target.value })
                            }
                        />
                    </Field>
                    <Button
                        variant="outline"
                        className="size-10 p-0"
                        aria-label="Next day"
                        onClick={() => setView({ date: addDays(date, 1) })}
                    >
                        <LuChevronRight aria-hidden className="size-4" />
                    </Button>
                    {date !== today && (
                        <Button
                            variant="outline"
                            className="h-10 px-4"
                            onClick={() => setView({ date: today })}
                        >
                            Today
                        </Button>
                    )}
                </div>
                <p className="pb-2 text-sm font-semibold">{dayTitle(date)}</p>
            </div>

            {plants.data && plants.data.items.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    There is no active plant yet. Add one under master data
                    first.
                </p>
            )}

            {plantId && (
                <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_21rem]">
                    <div className="space-y-4">
                        <div ref={formRef} className="scroll-mt-4 empty:hidden">
                            {editing && (
                                <PourForm
                                    key={`${editing.pour?.id ?? editing.target.orderLineId}`}
                                    plantId={plantId}
                                    date={date}
                                    target={editing.target}
                                    pour={editing.pour}
                                    choices={formChoices}
                                    onClose={() => setEditing(null)}
                                />
                            )}
                        </div>

                        {(shifts.isLoading || plans.isLoading) && (
                            <Skeleton className="h-40 w-full" />
                        )}
                        {(shifts.isError || plans.isError) && (
                            <p role="alert" className="text-sm text-late">
                                The plans could not be loaded.
                            </p>
                        )}
                        {shifts.data && activeShifts.length === 0 && (
                            <p className="text-sm text-muted-foreground">
                                This plant has no active shifts. Add its shifts
                                under master data to start planning.
                            </p>
                        )}
                        {plans.data &&
                            activeShifts.map((shift) => (
                                <ShiftSection
                                    key={shift.id}
                                    shift={shift}
                                    summary={planOf(shift.id)}
                                    plantId={plantId}
                                    date={date}
                                    onEditPour={editPour}
                                />
                            ))}
                    </div>

                    <ToPlanList
                        plantId={plantId}
                        date={date}
                        canPlan={canPlan}
                        activeLineId={
                            editing && !editing.pour
                                ? editing.target.orderLineId
                                : undefined
                        }
                        onPlan={(line) =>
                            open({
                                target: {
                                    orderLineId: line.orderLineId,
                                    title: `${line.orderNumber} · ${line.customerName}`,
                                    detail: `${line.siteName} · ${line.productCode}`,
                                    method: line.pourMethod,
                                    remaining: line.remainingCft,
                                },
                            })
                        }
                    />
                </div>
            )}
        </div>
    );
}
