import { useState, type ReactNode } from "react";
import { LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import {
    DriverForm,
    OperatorForm,
    VehicleForm,
} from "@/components/fleet/FleetForms";
import {
    useDriverList,
    useFleetPlants,
    useFleetSave,
    useOperators,
    useVehicles,
} from "@/components/fleet/useFleet";
import { OpButton as Button } from "@/components/operator/OpButton";
import {
    inputClass,
    nextButtonClass,
    panelClass,
    tabClass,
    tabRowClass,
} from "@/components/orders/styles";
import { StatusChip } from "@/components/plants/StatusChip";
import { Skeleton } from "@/components/ui/skeleton";
import {
    operatorBody,
    vehicleStatusLabel,
    type Driver,
    type PumpOperator,
    type Vehicle,
    type VehicleStatus,
    type VehicleType,
} from "@/lib/fleet";
import { cft } from "@/lib/format";
import { cn } from "@/lib/utils";

type Tab = "mixers" | "pumps" | "drivers" | "operators";

const tabs: { id: Tab; label: string }[] = [
    { id: "mixers", label: "Mixers" },
    { id: "pumps", label: "Pumps" },
    { id: "drivers", label: "Drivers" },
    { id: "operators", label: "Pump operators" },
];

// Mixers carry concrete and have a driver. Pumps place it and have a crew of pump operators.
export function Fleet() {
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.fleetManage);
    const [tab, setTab] = useState<Tab>("mixers");

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Fleet</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    Mixers and their drivers, pumps and their crews.
                </p>
            </header>

            <div role="tablist" aria-label="Fleet" className={tabRowClass}>
                {tabs.map((t) => (
                    <button
                        key={t.id}
                        type="button"
                        role="tab"
                        aria-selected={tab === t.id}
                        onClick={() => setTab(t.id)}
                        className={tabClass(tab === t.id)}
                    >
                        {t.label}
                    </button>
                ))}
            </div>

            {tab === "mixers" && (
                <VehicleTab type="TransitMixer" canManage={canManage} />
            )}
            {tab === "pumps" && (
                <VehicleTab type="Pump" canManage={canManage} />
            )}
            {tab === "drivers" && <DriverTab canManage={canManage} />}
            {tab === "operators" && <OperatorTab canManage={canManage} />}
        </div>
    );
}

function NewButton({ label, onClick }: { label: string; onClick: () => void }) {
    return (
        <Button className={cn("h-11 px-4", nextButtonClass)} onClick={onClick}>
            <LuPlus aria-hidden /> {label}
        </Button>
    );
}

function ErrorLine({ message }: { message: string }) {
    return (
        <p role="alert" className="text-sm text-late">
            {message}
        </p>
    );
}

function VehicleStatusChip({ status }: { status: VehicleStatus }) {
    return (
        <span
            className={cn(
                "inline-flex w-fit items-center rounded-sm border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
                status === "Available"
                    ? "border-ok/40 bg-ok/10 text-ok"
                    : status === "InService"
                      ? "border-signal bg-signal/15"
                      : "border-border bg-muted text-muted-foreground",
            )}
        >
            {vehicleStatusLabel[status]}
        </span>
    );
}

function VehicleTab({
    type,
    canManage,
}: {
    type: VehicleType;
    canManage: boolean;
}) {
    const isMixer = type === "TransitMixer";
    const [plantId, setPlantId] = useState("");
    const [editing, setEditing] = useState<Vehicle | "new" | null>(null);
    const vehicles = useVehicles(type, plantId);
    const plants = useFleetPlants();
    const drivers = useDriverList();
    const operators = useOperators();
    const allPumps = useVehicles("Pump", "");
    const items = vehicles.data?.items ?? [];
    const plantItems = plants.data?.items ?? [];
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <select
                    value={plantId}
                    onChange={(e) => setPlantId(e.target.value)}
                    aria-label="Plant"
                    className={cn(inputClass, "h-11 w-auto")}
                >
                    <option value="">All plants</option>
                    {plantItems.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} · {p.name}
                        </option>
                    ))}
                </select>
                {canManage && editing === null && (
                    <NewButton
                        label={isMixer ? "New mixer" : "New pump"}
                        onClick={() => setEditing("new")}
                    />
                )}
            </div>

            {editing !== null && (
                <VehicleForm
                    key={editing === "new" ? "new" : editing.id}
                    type={type}
                    vehicle={editing === "new" ? null : editing}
                    plants={plantItems}
                    drivers={drivers.data?.items ?? []}
                    onDone={close}
                    onCancel={close}
                />
            )}

            {vehicles.isLoading && <Skeleton className="h-40 w-full" />}
            {vehicles.isError && <ErrorLine message={vehicles.error.message} />}
            {vehicles.data && items.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    {isMixer ? "No mixers yet." : "No pumps yet."}
                </p>
            )}

            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((v) => (
                        <VehicleRow
                            key={v.id}
                            vehicle={v}
                            canManage={canManage}
                            crew={(operators.data?.items ?? []).filter(
                                (o) => o.pumpId === v.id,
                            )}
                            pumps={allPumps.data?.items ?? []}
                            onEdit={() => setEditing(v)}
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function VehicleRow({
    vehicle,
    canManage,
    crew,
    pumps,
    onEdit,
}: {
    vehicle: Vehicle;
    canManage: boolean;
    crew: PumpOperator[];
    pumps: Vehicle[];
    onEdit: () => void;
}) {
    const isPump = vehicle.type === "Pump";
    const [showCrew, setShowCrew] = useState(false);
    const [adding, setAdding] = useState(false);
    const details = [
        vehicle.plantCode,
        vehicle.plateNumber,
        vehicle.capacityCft != null ? `${cft(vehicle.capacityCft)} CFT` : null,
        isPump
            ? vehicle.crewCount === 1
                ? "1 operator"
                : `${vehicle.crewCount} operators`
            : (vehicle.defaultDriverName ?? "No driver"),
    ].filter(Boolean);

    return (
        <li className="space-y-3 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-semibold">
                        {vehicle.code}
                        <VehicleStatusChip status={vehicle.status} />
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {details.join(" · ")}
                    </span>
                </div>
                <div className="flex flex-wrap gap-2">
                    {isPump && (
                        <Button
                            variant="outline"
                            className="h-11"
                            aria-expanded={showCrew}
                            onClick={() => setShowCrew((s) => !s)}
                        >
                            {showCrew ? "Hide crew" : "Crew"}
                        </Button>
                    )}
                    {canManage && (
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={onEdit}
                        >
                            Edit
                        </Button>
                    )}
                </div>
            </div>

            {isPump && showCrew && (
                <div className="space-y-2 rounded-sm border bg-muted/30 p-3">
                    {crew.length === 0 && (
                        <p className="text-sm text-muted-foreground">
                            No operators on this pump yet.
                        </p>
                    )}
                    {crew.length > 0 && (
                        <ul className="space-y-1">
                            {crew.map((o) => (
                                <CrewLine
                                    key={o.id}
                                    operator={o}
                                    canManage={canManage}
                                />
                            ))}
                        </ul>
                    )}
                    {canManage && !adding && (
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={() => setAdding(true)}
                        >
                            <LuPlus aria-hidden /> Add operator
                        </Button>
                    )}
                    {adding && (
                        <OperatorForm
                            operator={null}
                            pumps={pumps}
                            pumpId={vehicle.id}
                            onDone={() => setAdding(false)}
                            onCancel={() => setAdding(false)}
                        />
                    )}
                </div>
            )}
        </li>
    );
}

// One crew member, with their phone (or a note that it is missing) and a way off the crew.
function CrewLine({
    operator,
    canManage,
}: {
    operator: PumpOperator;
    canManage: boolean;
}) {
    const save = useFleetSave("", `/api/pump-operators/${operator.id}`);
    const inactive = operator.status === "Inactive";

    return (
        <li className="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span className="flex flex-wrap items-center gap-2">
                <span className={cn("font-medium", inactive && "line-through")}>
                    {operator.name}
                </span>
                <PhoneText phone={operator.phone} />
                {inactive && <StatusChip status="Inactive" />}
            </span>
            {canManage && operator.phone && (
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={() =>
                        save.mutate(operatorBody(operator, { pumpId: null }))
                    }
                >
                    Take off crew
                </Button>
            )}
            {save.isError && <ErrorLine message={save.error.message} />}
        </li>
    );
}

function PhoneText({ phone }: { phone: string | null }) {
    return phone ? (
        <a href={`tel:${phone}`} className="underline">
            {phone}
        </a>
    ) : (
        <span className="font-semibold text-late">Phone missing</span>
    );
}

function DriverTab({ canManage }: { canManage: boolean }) {
    const drivers = useDriverList();
    const [editing, setEditing] = useState<Driver | "new" | null>(null);
    const items = drivers.data?.items ?? [];
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm text-muted-foreground">
                    Drivers go with transit mixers. Set a mixer's usual driver
                    on the Mixers tab.
                </p>
                {canManage && editing === null && (
                    <NewButton
                        label="New driver"
                        onClick={() => setEditing("new")}
                    />
                )}
            </div>
            {editing !== null && (
                <DriverForm
                    key={editing === "new" ? "new" : editing.id}
                    driver={editing === "new" ? null : editing}
                    onDone={close}
                    onCancel={close}
                />
            )}
            {drivers.isLoading && <Skeleton className="h-40 w-full" />}
            {drivers.isError && <ErrorLine message={drivers.error.message} />}
            {drivers.data && items.length === 0 && (
                <p className="text-sm text-muted-foreground">No drivers yet.</p>
            )}
            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((d) => (
                        <PersonRow
                            key={d.id}
                            name={d.name}
                            status={d.status}
                            line={
                                <>
                                    {d.phone ? (
                                        <PhoneText phone={d.phone} />
                                    ) : (
                                        "No phone"
                                    )}
                                    {" · "}
                                    {d.mixerCodes.length > 0
                                        ? `Drives ${d.mixerCodes.join(", ")}`
                                        : "No mixer"}
                                </>
                            }
                            canManage={canManage}
                            savePath={`/api/drivers/${d.id}`}
                            bodyFor={(status) => ({
                                name: d.name,
                                phone: d.phone,
                                status,
                            })}
                            onEdit={() => setEditing(d)}
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function OperatorTab({ canManage }: { canManage: boolean }) {
    const operators = useOperators();
    const pumps = useVehicles("Pump", "");
    const [editing, setEditing] = useState<PumpOperator | "new" | null>(null);
    const items = operators.data?.items ?? [];
    const missingPhones = items.filter(
        (o) => !o.phone && o.status === "Active",
    ).length;
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm text-muted-foreground">
                    The people who set up and run the pumps. Each is in one
                    pump's crew at most.
                </p>
                {canManage && editing === null && (
                    <NewButton
                        label="New operator"
                        onClick={() => setEditing("new")}
                    />
                )}
            </div>
            {missingPhones > 0 && (
                <p className="rounded-sm border border-late/40 bg-card p-3 text-sm">
                    {missingPhones === 1
                        ? "1 operator has no phone number yet. Edit them to add it."
                        : `${missingPhones} operators have no phone number yet. Edit each to add it.`}
                </p>
            )}
            {editing !== null && (
                <OperatorForm
                    key={editing === "new" ? "new" : editing.id}
                    operator={editing === "new" ? null : editing}
                    pumps={pumps.data?.items ?? []}
                    onDone={close}
                    onCancel={close}
                />
            )}
            {operators.isLoading && <Skeleton className="h-40 w-full" />}
            {operators.isError && (
                <ErrorLine message={operators.error.message} />
            )}
            {operators.data && items.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    No pump operators yet.
                </p>
            )}
            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((o) => (
                        <PersonRow
                            key={o.id}
                            name={o.name}
                            status={o.status}
                            line={
                                <>
                                    <PhoneText phone={o.phone} />
                                    {" · "}
                                    {o.pumpCode
                                        ? `Crew of ${o.pumpCode}`
                                        : "No pump"}
                                </>
                            }
                            canManage={canManage}
                            // The API needs a phone, so an operator without one is edited first.
                            savePath={
                                o.phone ? `/api/pump-operators/${o.id}` : null
                            }
                            bodyFor={(status) => operatorBody(o, { status })}
                            onEdit={() => setEditing(o)}
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function PersonRow({
    name,
    status,
    line,
    canManage,
    savePath,
    bodyFor,
    onEdit,
}: {
    name: string;
    status: "Active" | "Inactive";
    line: ReactNode;
    canManage: boolean;
    savePath: string | null;
    bodyFor: (status: "Active" | "Inactive") => unknown;
    onEdit: () => void;
}) {
    const save = useFleetSave("", savePath ?? "");
    const active = status === "Active";

    return (
        <li className="space-y-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {name}
                        {!active && <StatusChip status={status} />}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {line}
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
                        {savePath && (
                            <Button
                                variant="outline"
                                tone={active ? "danger" : undefined}
                                className="h-11"
                                disabled={save.isPending}
                                onClick={() =>
                                    save.mutate(
                                        bodyFor(active ? "Inactive" : "Active"),
                                    )
                                }
                            >
                                {active ? "Deactivate" : "Reactivate"}
                            </Button>
                        )}
                    </div>
                )}
            </div>
            {save.isError && <ErrorLine message={save.error.message} />}
        </li>
    );
}
