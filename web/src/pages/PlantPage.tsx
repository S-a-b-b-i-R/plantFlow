import { useState, type ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import { LuChevronLeft, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { panelClass } from "@/components/orders/styles";
import {
    BatchingPlantForm,
    PlantForm,
    ShiftForm,
} from "@/components/plants/PlantForms";
import { StatusChip } from "@/components/plants/StatusChip";
import {
    usePlant,
    usePlantSave,
    usePlantShifts,
    usePlantUsage,
} from "@/components/plants/usePlants";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import {
    hhmm,
    hoursLabel,
    usageParts,
    type BatchingPlant,
    type PlantShift,
    type PlantSummary,
} from "@/lib/plants";
import { cn } from "@/lib/utils";

type Editing =
    | { kind: "plant" }
    | { kind: "shift"; shift: PlantShift | null }
    | { kind: "batching"; batchingPlant: BatchingPlant | null }
    | null;

// One plant: its details, its shifts and its batching plants. Only one form is open at a time.
export function PlantPage() {
    const { id = "" } = useParams();
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.plantsManage);
    const detail = usePlant(id);
    const shifts = usePlantShifts(id);
    const [editing, setEditing] = useState<Editing>(null);
    const close = () => setEditing(null);

    if (detail.isLoading) {
        return (
            <div>
                <Skeleton className="h-60 w-full" />
            </div>
        );
    }
    if (detail.isError || !detail.data) {
        return (
            <div className="space-y-3">
                <BackLink />
                <p role="alert" className="text-sm text-late">
                    {detail.error?.message ?? "This plant was not found."}
                </p>
            </div>
        );
    }

    const plant = detail.data.plant;
    const batchingPlants = detail.data.batchingPlants;

    return (
        <div className="space-y-6">
            <div className="space-y-3">
                <BackLink />
                <header className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                        <p className="text-sm font-semibold text-muted-foreground">
                            {plant.code}
                        </p>
                        <h1 className="display flex flex-wrap items-center gap-3 text-4xl leading-none">
                            {plant.name}
                            <StatusChip status={plant.status} />
                        </h1>
                        {plant.address && (
                            <p className="mt-2 text-sm">{plant.address}</p>
                        )}
                        <p className="mt-1 text-sm text-muted-foreground">
                            {cft(plant.operatingHoursPerDay)} operating hours a
                            day · {cft(plant.dailyCapacityCft)} CFT a day
                        </p>
                    </div>
                    {canManage && editing?.kind !== "plant" && (
                        <div className="flex flex-wrap gap-2">
                            <Button
                                variant="outline"
                                className="h-11"
                                onClick={() => setEditing({ kind: "plant" })}
                            >
                                Edit plant
                            </Button>
                        </div>
                    )}
                </header>
                {editing?.kind === "plant" && (
                    <PlantForm plant={plant} onDone={close} onCancel={close} />
                )}
                {canManage && editing?.kind !== "plant" && (
                    <PlantStatus plant={plant} />
                )}
            </div>

            <section className="space-y-3">
                <SectionHeader
                    title="Shifts"
                    hint="The working periods of the plant, in plant time. Active shifts cannot overlap."
                    action={
                        canManage &&
                        editing?.kind !== "shift" && (
                            <Button
                                variant="outline"
                                className="h-11"
                                onClick={() =>
                                    setEditing({ kind: "shift", shift: null })
                                }
                            >
                                <LuPlus aria-hidden /> Add shift
                            </Button>
                        )
                    }
                />
                {editing?.kind === "shift" && (
                    <ShiftForm
                        key={editing.shift?.id ?? "new"}
                        plantId={plant.id}
                        shift={editing.shift}
                        onDone={close}
                        onCancel={close}
                    />
                )}
                {shifts.isLoading && <Skeleton className="h-24 w-full" />}
                {shifts.isError && (
                    <p role="alert" className="text-sm text-late">
                        {shifts.error.message}
                    </p>
                )}
                {shifts.data && shifts.data.length === 0 && (
                    <p className="text-sm text-muted-foreground">
                        No shifts yet. Planning needs at least one shift.
                    </p>
                )}
                {shifts.data && shifts.data.length > 0 && (
                    <ul className={cn(panelClass, "divide-y")}>
                        {shifts.data.map((shift) => (
                            <ShiftRow
                                key={shift.id}
                                shift={shift}
                                canManage={canManage}
                                onEdit={() =>
                                    setEditing({ kind: "shift", shift })
                                }
                            />
                        ))}
                    </ul>
                )}
            </section>

            <section className="space-y-3">
                <SectionHeader
                    title="Batching plants"
                    hint="The machines that mix the concrete. Active ones make up the daily capacity."
                    action={
                        canManage &&
                        editing?.kind !== "batching" && (
                            <Button
                                variant="outline"
                                className="h-11"
                                onClick={() =>
                                    setEditing({
                                        kind: "batching",
                                        batchingPlant: null,
                                    })
                                }
                            >
                                <LuPlus aria-hidden /> Add batching plant
                            </Button>
                        )
                    }
                />
                {editing?.kind === "batching" && (
                    <BatchingPlantForm
                        key={editing.batchingPlant?.id ?? "new"}
                        plantId={plant.id}
                        batchingPlant={editing.batchingPlant}
                        onDone={close}
                        onCancel={close}
                    />
                )}
                {batchingPlants.length === 0 && (
                    <p className="text-sm text-muted-foreground">
                        No batching plants yet. Without one the plant has no
                        capacity.
                    </p>
                )}
                {batchingPlants.length > 0 && (
                    <ul className={cn(panelClass, "divide-y")}>
                        {batchingPlants.map((batchingPlant) => (
                            <BatchingRow
                                key={batchingPlant.id}
                                plant={plant}
                                batchingPlant={batchingPlant}
                                canManage={canManage}
                                onEdit={() =>
                                    setEditing({
                                        kind: "batching",
                                        batchingPlant,
                                    })
                                }
                            />
                        ))}
                    </ul>
                )}
            </section>
        </div>
    );
}

function BackLink() {
    return (
        <Link
            to="/plants"
            className="inline-flex min-h-11 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
            <LuChevronLeft aria-hidden className="size-4" /> All plants
        </Link>
    );
}

function SectionHeader({
    title,
    hint,
    action,
}: {
    title: string;
    hint: string;
    action: ReactNode;
}) {
    return (
        <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
                <h2 className="display text-2xl leading-none">{title}</h2>
                <p className="mt-1 text-sm text-muted-foreground">{hint}</p>
            </div>
            {action}
        </div>
    );
}

// Deactivating warns with what still uses the plant, then lets the person go ahead.
function PlantStatus({ plant }: { plant: PlantSummary }) {
    const [confirming, setConfirming] = useState(false);
    const usage = usePlantUsage(plant.id, confirming);
    const save = usePlantSave("/api/plants", `/api/plants/${plant.id}`);
    const setStatus = (status: "Active" | "Inactive") =>
        save.mutate(
            {
                code: plant.code,
                name: plant.name,
                address: plant.address,
                timeZone: plant.timeZone,
                operatingHoursPerDay: plant.operatingHoursPerDay,
                status,
            },
            { onSuccess: () => setConfirming(false) },
        );
    const parts = usage.data ? usageParts(usage.data) : [];

    return (
        <div className="space-y-2">
            {plant.status === "Inactive" ? (
                <div className="flex flex-wrap items-center gap-3">
                    <p className="text-sm text-muted-foreground">
                        This plant is inactive. It cannot be picked for new
                        orders or plans.
                    </p>
                    <Button
                        variant="outline"
                        className="h-11"
                        disabled={save.isPending}
                        onClick={() => setStatus("Active")}
                    >
                        Reactivate plant
                    </Button>
                </div>
            ) : confirming ? (
                <div className={cn(panelClass, "space-y-3 border-late/40 p-4")}>
                    <p className="font-medium">Deactivate {plant.code}?</p>
                    {usage.isLoading && <Skeleton className="h-10 w-full" />}
                    {usage.isError && (
                        <p role="alert" className="text-sm text-late">
                            {usage.error.message}
                        </p>
                    )}
                    {usage.data &&
                        (parts.length > 0 ? (
                            <p className="text-sm">
                                <span className="font-semibold text-late">
                                    Still in use:
                                </span>{" "}
                                {parts.join(", ")}. These keep the plant, but it
                                can no longer be picked for new orders, plans or
                                user assignments.
                            </p>
                        ) : (
                            <p className="text-sm">
                                Nothing open uses this plant. It can no longer
                                be picked for new orders or plans.
                            </p>
                        ))}
                    <div className="flex flex-wrap gap-2">
                        <Button
                            tone="danger"
                            className="h-11"
                            disabled={!usage.data || save.isPending}
                            onClick={() => setStatus("Inactive")}
                        >
                            {save.isPending
                                ? "Deactivating…"
                                : parts.length > 0
                                  ? "Deactivate anyway"
                                  : "Deactivate plant"}
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
            ) : (
                <Button
                    variant="outline"
                    tone="danger"
                    className="h-11"
                    onClick={() => {
                        save.reset();
                        setConfirming(true);
                    }}
                >
                    Deactivate plant
                </Button>
            )}
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
        </div>
    );
}

function ShiftRow({
    shift,
    canManage,
    onEdit,
}: {
    shift: PlantShift;
    canManage: boolean;
    onEdit: () => void;
}) {
    const save = usePlantSave("", `/api/plant-shifts/${shift.id}`);
    const active = shift.status === "Active";

    return (
        <li className="space-y-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {shift.name}
                        {!active && <StatusChip status={shift.status} />}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {hhmm(shift.startTime)}–{hhmm(shift.endTime)}
                        {shift.crossesMidnight && " next day"} ·{" "}
                        {hoursLabel(shift.durationMinutes)}
                    </span>
                </div>
                {canManage && (
                    <div className="flex flex-wrap gap-2">
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={onEdit}
                        >
                            Edit
                        </Button>
                        <Button
                            variant="outline"
                            tone={active ? "danger" : undefined}
                            className="h-11"
                            disabled={save.isPending}
                            onClick={() =>
                                save.mutate({
                                    name: shift.name,
                                    startTime: shift.startTime,
                                    endTime: shift.endTime,
                                    status: active ? "Inactive" : "Active",
                                })
                            }
                        >
                            {active ? "Deactivate" : "Reactivate"}
                        </Button>
                    </div>
                )}
            </div>
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
        </li>
    );
}

// Deactivating a batching plant asks first and says how much daily capacity is lost.
function BatchingRow({
    plant,
    batchingPlant,
    canManage,
    onEdit,
}: {
    plant: PlantSummary;
    batchingPlant: BatchingPlant;
    canManage: boolean;
    onEdit: () => void;
}) {
    const [confirming, setConfirming] = useState(false);
    const save = usePlantSave("", `/api/batching-plants/${batchingPlant.id}`);
    const active = batchingPlant.status === "Active";
    const lost =
        batchingPlant.ratedCapacityCftPerHour * plant.operatingHoursPerDay;
    const setStatus = (status: "Active" | "Inactive") =>
        save.mutate(
            {
                code: batchingPlant.code,
                make: batchingPlant.make,
                model: batchingPlant.model,
                ratedCapacityCftPerHour: batchingPlant.ratedCapacityCftPerHour,
                batchDurationMinutes: batchingPlant.batchDurationMinutes,
                status,
            },
            { onSuccess: () => setConfirming(false) },
        );
    const machine = [batchingPlant.make, batchingPlant.model]
        .filter(Boolean)
        .join(" ");

    return (
        <li className="space-y-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {batchingPlant.code}
                        {machine && (
                            <span className="font-normal">{machine}</span>
                        )}
                        {!active && (
                            <StatusChip status={batchingPlant.status} />
                        )}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {cft(batchingPlant.ratedCapacityCftPerHour)} CFT an hour
                        · {batchingPlant.batchDurationMinutes} min a batch
                    </span>
                </div>
                {canManage && !confirming && (
                    <div className="flex flex-wrap gap-2">
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={onEdit}
                        >
                            Edit
                        </Button>
                        <Button
                            variant="outline"
                            tone={active ? "danger" : undefined}
                            className="h-11"
                            disabled={save.isPending}
                            onClick={() =>
                                active
                                    ? setConfirming(true)
                                    : setStatus("Active")
                            }
                        >
                            {active ? "Deactivate" : "Reactivate"}
                        </Button>
                    </div>
                )}
            </div>
            {confirming && (
                <div className="space-y-2 rounded-sm border border-late/40 p-3">
                    <p className="text-sm">
                        Daily capacity drops from {cft(plant.dailyCapacityCft)}{" "}
                        to {cft(Math.max(0, plant.dailyCapacityCft - lost))}{" "}
                        CFT.
                    </p>
                    <div className="flex flex-wrap gap-2">
                        <Button
                            tone="danger"
                            className="h-11"
                            disabled={save.isPending}
                            onClick={() => setStatus("Inactive")}
                        >
                            {save.isPending ? "Deactivating…" : "Deactivate"}
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
            )}
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
        </li>
    );
}
