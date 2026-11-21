import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
    textareaClass,
} from "@/components/orders/styles";
import { api, ApiError } from "@/lib/api";
import { cft, plantTime } from "@/lib/format";
import { methodLabel, type PourMethod } from "@/lib/orders";
import {
    hhmm,
    validQuantity,
    type PlantShift,
    type Pour,
    type ShiftPlanDetail,
} from "@/lib/planning";
import { cn } from "@/lib/utils";
import { usePumpCrew, usePumps } from "./usePlanning";

// The line being planned: from the list of orders to plan, or taken from the pour being edited.
export type PourTarget = {
    orderLineId: string;
    title: string;
    detail: string;
    method: PourMethod;
    // What is still unplanned on the line, when known.
    remaining: number | null;
};

// A shift that can take a pour: it has no plan yet, or its plan is a draft.
export type ShiftChoice = { shift: PlantShift; planId: string | null };

export function PourForm({
    plantId,
    date,
    target,
    pour,
    choices,
    onClose,
}: {
    plantId: string;
    date: string;
    target: PourTarget;
    pour?: Pour;
    choices: ShiftChoice[];
    onClose: () => void;
}) {
    const me = useMe().data!;
    const queryClient = useQueryClient();
    const pumps = usePumps(target.method === "Pump" ? plantId : undefined);

    const byPump = target.method === "Pump";
    const [shiftId, setShiftId] = useState(choices[0]?.shift.id ?? "");
    const choice = choices.find((c) => c.shift.id === shiftId);

    const [qty, setQty] = useState(
        String(pour ? pour.plannedCft : (target.remaining ?? "")),
    );
    const [start, setStart] = useState(
        pour
            ? plantTime(pour.plannedStart)
            : hhmm(choices[0]?.shift.startTime ?? "06:00"),
    );
    const [pumpId, setPumpId] = useState(pour?.pumpId ?? "");
    const [operatorId, setOperatorId] = useState(pour?.operatorId ?? "");
    const crew = usePumpCrew(pumpId);
    const [crewNotes, setCrewNotes] = useState(pour?.crewNotes ?? "");
    const [notes, setNotes] = useState(pour?.notes ?? "");

    // Set when the API says the pump has not rested long enough and this person may override it.
    const [askOverride, setAskOverride] = useState(false);
    const [reason, setReason] = useState("");
    const [error, setError] = useState<string | null>(null);

    const canOverride = can(me, PERMISSIONS.deliveriesPlanOverride);

    const save = useMutation({
        mutationFn: async () => {
            let planId = choice!.planId;
            if (!planId) {
                const created = await api<ShiftPlanDetail>("/api/shift-plans", {
                    method: "POST",
                    body: JSON.stringify({
                        plantId,
                        date,
                        shiftId: choice!.shift.id,
                    }),
                });
                planId = created.plan.id;
            }

            const body = {
                orderLineId: target.orderLineId,
                plannedCft: Number(qty),
                startTime: `${start}:00`,
                pumpId: byPump ? pumpId : null,
                operatorId: byPump && operatorId ? operatorId : null,
                crewNotes: byPump && crewNotes.trim() ? crewNotes.trim() : null,
                notes: notes.trim() || null,
            };
            const override = askOverride && reason.trim().length >= 3;
            const url = pour
                ? `/api/pours/${pour.id}`
                : `/api/shift-plans/${planId}/pours`;
            return api<unknown>(override ? `${url}/override` : url, {
                method: pour ? "PUT" : "POST",
                body: JSON.stringify(
                    override ? { reason: reason.trim(), pour: body } : body,
                ),
            });
        },
        onSuccess: onClose,
        onError: (e) => {
            setError(e.message);
            // The API words this refusal so that a plant manager can repeat the request with a reason.
            if (
                canOverride &&
                e instanceof ApiError &&
                e.status === 409 &&
                e.message.includes("override")
            ) {
                setAskOverride(true);
            }
        },
        onSettled: () =>
            queryClient.invalidateQueries({ queryKey: ["planning"] }),
    });

    const quantityOk =
        validQuantity(qty) &&
        (target.remaining === null || Number(qty) <= target.remaining);
    const ready =
        !!choice &&
        quantityOk &&
        /^\d\d:\d\d$/.test(start) &&
        (!byPump || !!pumpId) &&
        (!askOverride || reason.trim().length >= 3);

    const submit = (e: FormEvent) => {
        e.preventDefault();
        setError(null);
        save.mutate();
    };

    if (choices.length === 0) {
        return (
            <div className={cn(panelClass, "space-y-3 p-4")}>
                <p className="text-sm">
                    Every shift of this day already has a plan that is waiting
                    for approval or approved. Send one back or reopen it to add
                    a pour.
                </p>
                <Button
                    variant="outline"
                    className="h-10 px-4"
                    onClick={onClose}
                >
                    Close
                </Button>
            </div>
        );
    }

    const shift = choice?.shift;
    const usablePumps = (pumps.data?.items ?? []).filter(
        (p) => p.status !== "Maintenance" && p.status !== "Retired",
    );

    return (
        <form onSubmit={submit} className={cn(panelClass, "space-y-4 p-4")}>
            <div>
                <h2 className="display text-2xl leading-none">
                    {pour ? "Change this pour" : "Plan a pour"}
                </h2>
                <p className="mt-1 text-sm font-semibold">{target.title}</p>
                <p className="text-sm text-muted-foreground">
                    {target.detail} · {methodLabel[target.method]}
                    {target.remaining !== null &&
                        ` · ${cft(target.remaining)} CFT left to plan`}
                </p>
            </div>

            <div className="grid gap-4 sm:grid-cols-3">
                {pour || choices.length === 1 ? (
                    <div className="text-sm">
                        <span className="mb-1 block font-medium">Shift</span>
                        <span className="flex h-10 items-center">
                            {shift?.name}
                        </span>
                    </div>
                ) : (
                    <Field label="Shift">
                        <select
                            className={inputClass}
                            value={shiftId}
                            onChange={(e) => {
                                setShiftId(e.target.value);
                                const next = choices.find(
                                    (c) => c.shift.id === e.target.value,
                                );
                                if (next) setStart(hhmm(next.shift.startTime));
                            }}
                        >
                            {choices.map((c) => (
                                <option key={c.shift.id} value={c.shift.id}>
                                    {c.shift.name}
                                    {c.planId ? "" : " (no plan yet)"}
                                </option>
                            ))}
                        </select>
                    </Field>
                )}
                <Field
                    label="Quantity (CFT)"
                    hint={
                        qty && !quantityOk
                            ? target.remaining !== null &&
                              validQuantity(qty) &&
                              Number(qty) > target.remaining
                                ? "More than is left to plan."
                                : "Use a number above 0 with at most 2 decimals."
                            : undefined
                    }
                >
                    <input
                        inputMode="decimal"
                        className={inputClass}
                        value={qty}
                        onChange={(e) => setQty(e.target.value)}
                    />
                </Field>
                <Field
                    label="Casting start"
                    hint={
                        shift
                            ? `The ${shift.name} shift runs ${hhmm(shift.startTime)}–${hhmm(shift.endTime)}.`
                            : undefined
                    }
                >
                    <input
                        type="time"
                        className={inputClass}
                        value={start}
                        onChange={(e) => setStart(e.target.value)}
                    />
                </Field>
            </div>

            {byPump && (
                <div className="grid gap-4 sm:grid-cols-3">
                    <Field label="Pump">
                        <select
                            className={inputClass}
                            value={pumpId}
                            onChange={(e) => {
                                // The operator comes from the pump's crew, so a new pump clears it.
                                setPumpId(e.target.value);
                                setOperatorId("");
                            }}
                        >
                            <option value="">Choose a pump</option>
                            {usablePumps.map((p) => (
                                <option key={p.id} value={p.id}>
                                    {p.code}
                                    {p.status === "InService"
                                        ? " (in service)"
                                        : ""}
                                </option>
                            ))}
                        </select>
                    </Field>
                    <Field
                        label="Operator"
                        hint={
                            pumpId && crew.data?.length === 0
                                ? "This pump has no crew yet. Add operators on the Fleet screen."
                                : "Optional. Picked from the pump's crew."
                        }
                    >
                        <select
                            className={inputClass}
                            value={operatorId}
                            onChange={(e) => setOperatorId(e.target.value)}
                            disabled={!pumpId}
                        >
                            <option value="">No operator named</option>
                            {(crew.data ?? []).map((o) => (
                                <option key={o.id} value={o.id}>
                                    {o.name}
                                </option>
                            ))}
                        </select>
                    </Field>
                    <Field label="Crew">
                        <input
                            className={inputClass}
                            maxLength={500}
                            value={crewNotes}
                            onChange={(e) => setCrewNotes(e.target.value)}
                            placeholder="Names or a short note"
                        />
                    </Field>
                </div>
            )}

            <Field label="Notes for the plant">
                <textarea
                    rows={2}
                    maxLength={500}
                    className={textareaClass}
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                />
            </Field>

            {askOverride && (
                <div className="rounded-sm border border-late/40 bg-late/5 p-3">
                    <Field
                        label="Why use this pump before it has rested?"
                        hint="The reason is kept on the pour."
                    >
                        <input
                            autoFocus
                            className={inputClass}
                            maxLength={500}
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                        />
                    </Field>
                </div>
            )}

            {error && (
                <p role="alert" className="text-sm text-late">
                    {error}
                </p>
            )}

            <div className="flex flex-wrap gap-2">
                <Button
                    type="submit"
                    className={cn("h-10 px-5", nextButtonClass)}
                    disabled={!ready || save.isPending}
                >
                    {save.isPending
                        ? "Saving…"
                        : askOverride
                          ? "Plan with override"
                          : pour
                            ? "Save changes"
                            : "Add pour"}
                </Button>
                <Button
                    type="button"
                    variant="outline"
                    className="h-10 px-4"
                    onClick={onClose}
                >
                    Never mind
                </Button>
            </div>
        </form>
    );
}
