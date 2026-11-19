import { useState } from "react";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import {
    overheadKindLabel,
    unitLabel,
    validAmount,
    type BandRow,
    type MaterialCostRow,
    type OverheadKind,
    type OverheadRow,
} from "@/lib/pricing";
import { FormFrame } from "./FormFrame";
import type { FormProps } from "./kind";
import { useBookSave, useMaterials, usePlants } from "./useBook";

const kinds = Object.keys(overheadKindLabel) as OverheadKind[];

// A distance can be zero, so it is checked on its own.
const validKm = (text: string) => {
    const value = Number(text);
    return (
        text.trim() !== "" &&
        Number.isFinite(value) &&
        value >= 0 &&
        Math.abs(value - Number(value.toFixed(2))) < 1e-9
    );
};

export function MaterialCostForm({ item, onDone, onCancel }: FormProps) {
    const row = item as MaterialCostRow | null;
    const materials = useMaterials();
    const plants = usePlants();
    const save = useBookSave("/api/material-costs", row?.id ?? null);
    const [materialId, setMaterialId] = useState(row?.materialId ?? "");
    const [plantId, setPlantId] = useState(row?.plantId ?? "");
    const [cost, setCost] = useState(row ? String(row.costPerUnit) : "");
    const [from, setFrom] = useState(row?.effectiveFrom ?? "");

    const unit =
        row?.unit ??
        materials.data?.items.find((m) => m.id === materialId)?.unit;
    const body = row
        ? { costPerUnit: Number(cost), effectiveFrom: from }
        : {
              materialId,
              plantId: plantId || null,
              costPerUnit: Number(cost),
              effectiveFrom: from,
          };

    return (
        <FormFrame
            title={row ? "Edit material cost" : "New material cost"}
            canSave={
                !!(row || materialId) && validAmount(cost, 4) && from !== ""
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Material">
                <select
                    value={materialId}
                    disabled={!!row}
                    onChange={(e) => setMaterialId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">Choose a material</option>
                    {row &&
                        !materials.data?.items.some(
                            (m) => m.id === row.materialId,
                        ) && (
                            <option value={row.materialId}>
                                {row.materialCode} {row.materialName}
                            </option>
                        )}
                    {materials.data?.items.map((m) => (
                        <option key={m.id} value={m.id}>
                            {m.code} {m.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field
                label="Plant"
                hint="A plant's own cost beats the one for all plants."
            >
                <select
                    value={plantId}
                    disabled={!!row}
                    onChange={(e) => setPlantId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">All plants</option>
                    {row?.plantId &&
                        !plants.data?.items.some(
                            (p) => p.id === row.plantId,
                        ) && (
                            <option value={row.plantId}>{row.plantCode}</option>
                        )}
                    {plants.data?.items.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field
                label={unit ? `Cost per ${unitLabel[unit]}` : "Cost per unit"}
                hint="Up to 4 decimals."
            >
                <input
                    inputMode="decimal"
                    value={cost}
                    onChange={(e) => setCost(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Counts from"
                hint="The latest approved cost on or before the order date is used."
            >
                <input
                    type="date"
                    value={from}
                    onChange={(e) => setFrom(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}

export function OverheadForm({ item, onDone, onCancel }: FormProps) {
    const row = item as OverheadRow | null;
    const plants = usePlants();
    const save = useBookSave("/api/overhead-costs", row?.id ?? null);
    const [kind, setKind] = useState<OverheadKind>(row?.kind ?? "Overhead");
    const [name, setName] = useState(row?.name ?? "");
    const [plantId, setPlantId] = useState(row?.plantId ?? "");
    const [amount, setAmount] = useState(row ? String(row.amountPerCft) : "");
    const [from, setFrom] = useState(row?.validFrom ?? "");
    const [to, setTo] = useState(row?.validTo ?? "");

    const body = {
        kind,
        name: name.trim(),
        plantId: plantId || null,
        amountPerCft: Number(amount),
        validFrom: from,
        validTo: to || null,
    };

    return (
        <FormFrame
            title={row ? "Edit overhead" : "New overhead"}
            canSave={
                name.trim().length >= 2 &&
                validAmount(amount, 4) &&
                from !== "" &&
                (!to || to >= from) &&
                (kind !== "BridgeToll" || plantId !== "")
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Type">
                <select
                    value={kind}
                    onChange={(e) => setKind(e.target.value as OverheadKind)}
                    className={inputClass}
                >
                    {kinds.map((k) => (
                        <option key={k} value={k}>
                            {overheadKindLabel[k]}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={100}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Plant"
                hint={
                    kind === "BridgeToll"
                        ? "A bridge toll belongs to one plant."
                        : "Leave on all plants unless it applies to one."
                }
            >
                <select
                    value={plantId}
                    onChange={(e) => setPlantId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">All plants</option>
                    {row?.plantId &&
                        !plants.data?.items.some(
                            (p) => p.id === row.plantId,
                        ) && (
                            <option value={row.plantId}>{row.plantCode}</option>
                        )}
                    {plants.data?.items.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Amount per CFT" hint="Up to 4 decimals.">
                <input
                    inputMode="decimal"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Counts from">
                <input
                    type="date"
                    value={from}
                    onChange={(e) => setFrom(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Counts until" hint="Leave empty for no end date.">
                <input
                    type="date"
                    value={to}
                    onChange={(e) => setTo(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}

export function BandForm({ item, onDone, onCancel }: FormProps) {
    const row = item as BandRow | null;
    const plants = usePlants();
    const save = useBookSave("/api/delivery-charges", row?.id ?? null);
    const [plantId, setPlantId] = useState(row?.plantId ?? "");
    const [fromKm, setFromKm] = useState(row ? String(row.fromKm) : "");
    const [toKm, setToKm] = useState(row?.toKm != null ? String(row.toKm) : "");
    const [charge, setCharge] = useState(row ? String(row.chargePerCft) : "");

    const body = {
        plantId: plantId || null,
        fromKm: Number(fromKm),
        toKm: toKm.trim() ? Number(toKm) : null,
        chargePerCft: Number(charge),
    };

    return (
        <FormFrame
            title={row ? "Edit delivery band" : "New delivery band"}
            canSave={
                validKm(fromKm) &&
                (!toKm.trim() ||
                    (validKm(toKm) && Number(toKm) > Number(fromKm))) &&
                validAmount(charge, 4)
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field
                label="Plant"
                hint="A plant with bands of its own ignores the general ones."
            >
                <select
                    value={plantId}
                    onChange={(e) => setPlantId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">All plants</option>
                    {row?.plantId &&
                        !plants.data?.items.some(
                            (p) => p.id === row.plantId,
                        ) && (
                            <option value={row.plantId}>{row.plantCode}</option>
                        )}
                    {plants.data?.items.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Charge per CFT" hint="Up to 4 decimals.">
                <input
                    inputMode="decimal"
                    value={charge}
                    onChange={(e) => setCharge(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Over (km)"
                hint="15 means there is no charge up to and including 15 km."
            >
                <input
                    inputMode="decimal"
                    value={fromKm}
                    onChange={(e) => setFromKm(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Up to and including (km)"
                hint="Leave empty for no limit."
            >
                <input
                    inputMode="decimal"
                    value={toKm}
                    onChange={(e) => setToKm(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}
