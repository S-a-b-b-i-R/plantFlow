import { useState } from "react";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import { FormFrame } from "@/components/pricing/FormFrame";
import {
    vehicleStatuses,
    vehicleStatusLabel,
    type Driver,
    type PumpOperator,
    type Vehicle,
    type VehicleStatus,
    type VehicleType,
} from "@/lib/fleet";
import type { PlantOption } from "@/lib/orders";
import { codePattern } from "@/lib/plants";
import { useFleetSave } from "./useFleet";

const phoneLooksValid = (value: string) => value.trim().length >= 5;

// Add or edit a mixer or a pump. A mixer has a capacity and a driver; a pump has an assembly time,
// and its crew are pump operators, managed on the pump's row.
export function VehicleForm({
    type,
    vehicle,
    plants,
    drivers,
    onDone,
    onCancel,
}: {
    type: VehicleType;
    vehicle: Vehicle | null;
    plants: PlantOption[];
    drivers: Driver[];
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useFleetSave(
        "/api/vehicles",
        vehicle ? `/api/vehicles/${vehicle.id}` : null,
    );
    const isMixer = type === "TransitMixer";
    const [plantId, setPlantId] = useState(
        vehicle?.plantId ?? plants[0]?.id ?? "",
    );
    const [code, setCode] = useState(vehicle?.code ?? "");
    const [plate, setPlate] = useState(vehicle?.plateNumber ?? "");
    const [capacity, setCapacity] = useState(
        vehicle?.capacityCft != null ? String(vehicle.capacityCft) : "",
    );
    const [driverId, setDriverId] = useState(vehicle?.defaultDriverId ?? "");
    const [assembly, setAssembly] = useState(
        vehicle?.assemblyMinutes != null ? String(vehicle.assemblyMinutes) : "",
    );
    const [status, setStatus] = useState<VehicleStatus>(
        vehicle?.status ?? "Available",
    );

    const capacityValue = capacity.trim() === "" ? null : Number(capacity);
    const assemblyValue = assembly.trim() === "" ? null : Number(assembly);
    const body = {
        plantId,
        code: code.trim(),
        type,
        capacityCft: capacityValue,
        plateNumber: plate.trim() || null,
        defaultDriverId: isMixer ? driverId || null : null,
        status,
        assemblyMinutes: isMixer ? null : assemblyValue,
    };
    const capacityOk = isMixer
        ? capacityValue !== null && capacityValue > 0
        : capacityValue === null || capacityValue > 0;
    const assemblyOk =
        assemblyValue === null ||
        (Number.isInteger(assemblyValue) &&
            assemblyValue >= 0 &&
            assemblyValue <= 480);
    const noun = isMixer ? "mixer" : "pump";
    const activeDrivers = drivers.filter(
        (d) => d.status === "Active" || d.id === vehicle?.defaultDriverId,
    );

    return (
        <FormFrame
            title={vehicle ? `Edit ${noun}` : `New ${noun}`}
            saveLabel={`Save ${noun}`}
            canSave={
                !!plantId &&
                codePattern.test(body.code) &&
                capacityOk &&
                assemblyOk
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Code" hint="Letters, numbers and dashes.">
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Plant">
                <select
                    value={plantId}
                    onChange={(e) => setPlantId(e.target.value)}
                    className={inputClass}
                >
                    {vehicle &&
                        !plants.some((p) => p.id === vehicle.plantId) && (
                            <option value={vehicle.plantId}>
                                {vehicle.plantCode} (inactive)
                            </option>
                        )}
                    {plants.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} · {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Plate number">
                <input
                    value={plate}
                    onChange={(e) => setPlate(e.target.value)}
                    maxLength={30}
                    className={inputClass}
                />
            </Field>
            <Field
                label={isMixer ? "Capacity (CFT)" : "Capacity (CFT, optional)"}
            >
                <input
                    inputMode="decimal"
                    value={capacity}
                    onChange={(e) => setCapacity(e.target.value)}
                    className={inputClass}
                />
            </Field>
            {isMixer ? (
                <Field label="Usual driver">
                    <select
                        value={driverId}
                        onChange={(e) => setDriverId(e.target.value)}
                        className={inputClass}
                    >
                        <option value="">No driver</option>
                        {activeDrivers.map((d) => (
                            <option key={d.id} value={d.id}>
                                {d.name}
                                {d.status === "Inactive" ? " (inactive)" : ""}
                            </option>
                        ))}
                    </select>
                </Field>
            ) : (
                <Field
                    label="Assembly minutes"
                    hint="Time to set the pump up on site. Leave empty for the company default."
                >
                    <input
                        inputMode="numeric"
                        value={assembly}
                        onChange={(e) => setAssembly(e.target.value)}
                        className={inputClass}
                    />
                </Field>
            )}
            <Field
                label="Status"
                hint="A pump with pours ahead, or a mixer out with a load, can't go for maintenance or be retired."
            >
                <select
                    value={status}
                    onChange={(e) => setStatus(e.target.value as VehicleStatus)}
                    className={inputClass}
                >
                    {vehicleStatuses.map((s) => (
                        <option key={s} value={s}>
                            {vehicleStatusLabel[s]}
                        </option>
                    ))}
                </select>
            </Field>
        </FormFrame>
    );
}

export function DriverForm({
    driver,
    onDone,
    onCancel,
}: {
    driver: Driver | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useFleetSave(
        "/api/drivers",
        driver ? `/api/drivers/${driver.id}` : null,
    );
    const [name, setName] = useState(driver?.name ?? "");
    const [phone, setPhone] = useState(driver?.phone ?? "");
    const body = {
        name: name.trim(),
        phone: phone.trim() || null,
        status: driver?.status ?? "Active",
    };

    return (
        <FormFrame
            title={driver ? "Edit driver" : "New driver"}
            saveLabel="Save driver"
            canSave={body.name.length >= 2}
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Phone">
                <input
                    type="tel"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    maxLength={30}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}

// Name and phone are required. The pump puts the operator in that pump's crew (and takes them off another).
export function OperatorForm({
    operator,
    pumps,
    pumpId: fixedPumpId,
    onDone,
    onCancel,
}: {
    operator: PumpOperator | null;
    pumps: Vehicle[];
    // When adding from a pump's row, the operator joins that pump.
    pumpId?: string;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useFleetSave(
        "/api/pump-operators",
        operator ? `/api/pump-operators/${operator.id}` : null,
    );
    const [name, setName] = useState(operator?.name ?? "");
    const [phone, setPhone] = useState(operator?.phone ?? "");
    const [pumpId, setPumpId] = useState(operator?.pumpId ?? fixedPumpId ?? "");
    const body = {
        name: name.trim(),
        phone: phone.trim(),
        pumpId: pumpId || null,
        status: operator?.status ?? "Active",
    };
    const usablePumps = pumps.filter(
        (p) => p.status !== "Retired" || p.id === operator?.pumpId,
    );

    return (
        <FormFrame
            title={operator ? "Edit pump operator" : "New pump operator"}
            saveLabel="Save operator"
            canSave={body.name.length >= 2 && phoneLooksValid(body.phone)}
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={100}
                    className={inputClass}
                />
            </Field>
            <Field label="Phone" hint="Needed so the plant can reach them.">
                <input
                    type="tel"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    maxLength={30}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Pump crew"
                hint="An operator is in one pump's crew at most. Picking another pump moves them."
            >
                <select
                    value={pumpId}
                    onChange={(e) => setPumpId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">No pump</option>
                    {usablePumps.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} · {p.plantCode}
                        </option>
                    ))}
                </select>
            </Field>
        </FormFrame>
    );
}
