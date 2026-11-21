import { useState } from "react";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import { FormFrame } from "@/components/pricing/FormFrame";
import { FormShell } from "@/components/pricing/FormShell";
import {
    codePattern,
    hhmm,
    withSeconds,
    type BatchingPlant,
    type PlantDetail,
    type PlantShift,
    type PlantSummary,
} from "@/lib/plants";
import { usePlantSave } from "./usePlants";

const hoursValid = (value: string) => {
    const n = Number(value);
    return value.trim() !== "" && Number.isFinite(n) && n >= 1 && n <= 24;
};

// Create or edit a plant. Status is changed with Deactivate and Reactivate, not here.
// The time zone is not shown: plant time comes from the company's UTC offset in settings.
export function PlantForm({
    plant,
    onDone,
    onCancel,
}: {
    plant: PlantSummary | null;
    onDone: (saved: PlantDetail | null) => void;
    onCancel: () => void;
}) {
    const save = usePlantSave<PlantDetail | undefined>(
        "/api/plants",
        plant ? `/api/plants/${plant.id}` : null,
    );
    const [code, setCode] = useState(plant?.code ?? "");
    const [name, setName] = useState(plant?.name ?? "");
    const [address, setAddress] = useState(plant?.address ?? "");
    const [hours, setHours] = useState(
        String(plant?.operatingHoursPerDay ?? 10),
    );

    const body = {
        code: code.trim(),
        name: name.trim(),
        address: address.trim() || null,
        timeZone: plant?.timeZone ?? "UTC",
        operatingHoursPerDay: Number(hours),
        status: plant?.status ?? "Active",
    };

    return (
        <FormShell
            title={plant ? "Edit plant" : "New plant"}
            saveLabel={plant ? "Save plant" : "Add plant"}
            canSave={
                codePattern.test(body.code) &&
                body.name.length >= 2 &&
                hoursValid(hours)
            }
            saving={save.isPending}
            error={save.isError ? save.error.message : null}
            onSave={() =>
                save.mutate(body, {
                    onSuccess: (saved) => onDone(saved ?? null),
                })
            }
            onCancel={onCancel}
        >
            <Field
                label="Code"
                hint="Short and unique, for example DHK-1. Letters, digits and dashes."
            >
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value.toUpperCase())}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Address" className="sm:col-span-2">
                <input
                    value={address}
                    onChange={(e) => setAddress(e.target.value)}
                    maxLength={500}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Operating hours per day"
                hint="Between 1 and 24. Daily capacity is the batching plants' hourly capacity times these hours."
            >
                <input
                    inputMode="decimal"
                    value={hours}
                    onChange={(e) => setHours(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormShell>
    );
}

export function ShiftForm({
    plantId,
    shift,
    onDone,
    onCancel,
}: {
    plantId: string;
    shift: PlantShift | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = usePlantSave(
        `/api/plants/${plantId}/shifts`,
        shift ? `/api/plant-shifts/${shift.id}` : null,
    );
    const [name, setName] = useState(shift?.name ?? "");
    const [start, setStart] = useState(shift ? hhmm(shift.startTime) : "");
    const [end, setEnd] = useState(shift ? hhmm(shift.endTime) : "");

    const body = {
        name: name.trim(),
        startTime: withSeconds(start),
        endTime: withSeconds(end),
        status: shift?.status ?? "Active",
    };
    const crosses = start !== "" && end !== "" && end <= start;

    return (
        <FormFrame
            title={shift ? "Edit shift" : "New shift"}
            saveLabel="Save shift"
            canSave={
                body.name.length >= 2 &&
                start !== "" &&
                end !== "" &&
                start !== end
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Name" className="sm:col-span-2">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={50}
                    placeholder="Morning"
                    className={inputClass}
                />
            </Field>
            <Field label="Starts" hint="Plant time.">
                <input
                    type="time"
                    value={start}
                    onChange={(e) => setStart(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Ends"
                hint={
                    crosses
                        ? "Ends the next day. The shift belongs to the day it starts."
                        : "An end before the start runs past midnight."
                }
            >
                <input
                    type="time"
                    value={end}
                    onChange={(e) => setEnd(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}

export function BatchingPlantForm({
    plantId,
    batchingPlant,
    onDone,
    onCancel,
}: {
    plantId: string;
    batchingPlant: BatchingPlant | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = usePlantSave(
        `/api/plants/${plantId}/batching-plants`,
        batchingPlant ? `/api/batching-plants/${batchingPlant.id}` : null,
    );
    const [code, setCode] = useState(batchingPlant?.code ?? "");
    const [make, setMake] = useState(batchingPlant?.make ?? "");
    const [model, setModel] = useState(batchingPlant?.model ?? "");
    const [capacity, setCapacity] = useState(
        batchingPlant ? String(batchingPlant.ratedCapacityCftPerHour) : "",
    );
    const [batchMinutes, setBatchMinutes] = useState(
        String(batchingPlant?.batchDurationMinutes ?? 8),
    );

    const body = {
        code: code.trim(),
        make: make.trim() || null,
        model: model.trim() || null,
        ratedCapacityCftPerHour: Number(capacity),
        batchDurationMinutes: Number(batchMinutes),
        status: batchingPlant?.status ?? "Active",
    };

    return (
        <FormFrame
            title={batchingPlant ? "Edit batching plant" : "New batching plant"}
            saveLabel="Save batching plant"
            canSave={
                codePattern.test(body.code) &&
                body.ratedCapacityCftPerHour > 0 &&
                Number.isInteger(body.batchDurationMinutes) &&
                body.batchDurationMinutes >= 1 &&
                body.batchDurationMinutes <= 60
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field
                label="Code"
                hint="Unique within this plant, for example BP1."
            >
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value.toUpperCase())}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Rated capacity" hint="CFT per hour.">
                <input
                    inputMode="decimal"
                    value={capacity}
                    onChange={(e) => setCapacity(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Make">
                <input
                    value={make}
                    onChange={(e) => setMake(e.target.value)}
                    maxLength={100}
                    className={inputClass}
                />
            </Field>
            <Field label="Model">
                <input
                    value={model}
                    onChange={(e) => setModel(e.target.value)}
                    maxLength={100}
                    className={inputClass}
                />
            </Field>
            <Field label="Minutes per batch" hint="Between 1 and 60.">
                <input
                    inputMode="numeric"
                    value={batchMinutes}
                    onChange={(e) => setBatchMinutes(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}
