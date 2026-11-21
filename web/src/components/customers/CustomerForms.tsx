import { useState } from "react";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    textareaClass,
} from "@/components/orders/styles";
import { FormFrame } from "@/components/pricing/FormFrame";
import { FormShell } from "@/components/pricing/FormShell";
import {
    customerTypes,
    type Customer,
    type CustomerType,
    type SalesRep,
    type Site,
    type SiteDistance,
} from "@/lib/customers";
import type { PlantOption } from "@/lib/orders";
import { codePattern } from "@/lib/plants";
import { cn } from "@/lib/utils";
import { useCustomerSave, useSalesReps } from "./useCustomers";

const emailLooksValid = (value: string) =>
    value === "" || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);

// Create or edit a customer. VAT exemption and status have their own controls.
// Only someone who assigns reps sees the rep field; a sales rep's new customer is their own.
export function CustomerForm({
    customer,
    canAssign,
    onDone,
    onCancel,
}: {
    customer: Customer | null;
    canAssign: boolean;
    onDone: (saved: Customer | null) => void;
    onCancel: () => void;
}) {
    const save = useCustomerSave<Customer | undefined>(
        "/api/customers",
        customer ? `/api/customers/${customer.id}` : null,
    );
    const reps = useSalesReps("Active");
    const [code, setCode] = useState(customer?.code ?? "");
    const [name, setName] = useState(customer?.name ?? "");
    const [type, setType] = useState<CustomerType>(
        customer?.type ?? "Contractor",
    );
    const [phone, setPhone] = useState(customer?.phone ?? "");
    const [email, setEmail] = useState(customer?.email ?? "");
    const [salesRepId, setSalesRepId] = useState(customer?.salesRepId ?? "");

    const body = {
        code: code.trim(),
        name: name.trim(),
        type,
        phone: phone.trim() || null,
        email: email.trim() || null,
        salesRepId: canAssign
            ? salesRepId || null
            : (customer?.salesRepId ?? null),
        status: customer?.status ?? "Active",
        vatExempt: customer?.vatExempt ?? false,
    };
    const repOptions = reps.data?.items ?? [];

    return (
        <FormShell
            title={customer ? "Edit customer" : "New customer"}
            saveLabel={customer ? "Save customer" : "Add customer"}
            canSave={
                codePattern.test(body.code) &&
                body.name.length >= 2 &&
                emailLooksValid(email.trim())
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
                hint="Short and unique, for example ABC-01. Letters, digits and dashes."
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
            <Field label="Type" hint="Sets the AIT rule in pricing.">
                <select
                    value={type}
                    onChange={(e) => setType(e.target.value as CustomerType)}
                    className={inputClass}
                >
                    {customerTypes.map((t) => (
                        <option key={t} value={t}>
                            {t}
                        </option>
                    ))}
                </select>
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
            <Field label="Email">
                <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            {canAssign ? (
                <Field
                    label="Sales rep"
                    hint="Who looks after this customer. Leave empty for none."
                >
                    <select
                        value={salesRepId}
                        onChange={(e) => setSalesRepId(e.target.value)}
                        className={inputClass}
                    >
                        <option value="">No sales rep</option>
                        {customer?.salesRepId &&
                            !repOptions.some(
                                (r) => r.id === customer.salesRepId,
                            ) && (
                                <option value={customer.salesRepId}>
                                    {customer.salesRepName ?? "Current rep"}{" "}
                                    (inactive)
                                </option>
                            )}
                        {repOptions.map((r) => (
                            <option key={r.id} value={r.id}>
                                {r.name}
                                {r.region ? ` · ${r.region}` : ""}
                            </option>
                        ))}
                    </select>
                </Field>
            ) : (
                !customer && (
                    <p className="self-end text-sm text-muted-foreground sm:pb-2">
                        You are this customer's sales rep.
                    </p>
                )
            )}
        </FormShell>
    );
}

const coordinate = (value: string) => {
    if (value.trim() === "") return null;
    const n = Number(value);
    return Number.isFinite(n) ? n : NaN;
};

export function SiteForm({
    customerId,
    site,
    onDone,
    onCancel,
}: {
    customerId: string;
    site: Site | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useCustomerSave(
        `/api/customers/${customerId}/sites`,
        site ? `/api/sites/${site.id}` : null,
    );
    const [name, setName] = useState(site?.name ?? "");
    const [address, setAddress] = useState(site?.address ?? "");
    const [lat, setLat] = useState(site?.latitude?.toString() ?? "");
    const [lng, setLng] = useState(site?.longitude?.toString() ?? "");
    const [contactName, setContactName] = useState(site?.contactName ?? "");
    const [contactPhone, setContactPhone] = useState(site?.contactPhone ?? "");
    const [accessNotes, setAccessNotes] = useState(site?.accessNotes ?? "");

    const latitude = coordinate(lat);
    const longitude = coordinate(lng);
    const coordinatesValid =
        (latitude === null) === (longitude === null) &&
        (latitude === null || (latitude >= -90 && latitude <= 90)) &&
        (longitude === null || (longitude >= -180 && longitude <= 180));

    const body = {
        name: name.trim(),
        address: address.trim() || null,
        latitude,
        longitude,
        contactName: contactName.trim() || null,
        contactPhone: contactPhone.trim() || null,
        accessNotes: accessNotes.trim() || null,
        status: site?.status ?? "Active",
    };

    return (
        <FormFrame
            title={site ? "Edit site" : "New site"}
            saveLabel="Save site"
            canSave={body.name.length >= 2 && coordinatesValid}
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field
                label="Name"
                hint="Unique for this customer, for example Tower A."
            >
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Address">
                <input
                    value={address}
                    onChange={(e) => setAddress(e.target.value)}
                    maxLength={500}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Latitude"
                hint="Give both or neither, for example 23.8103."
            >
                <input
                    inputMode="decimal"
                    value={lat}
                    onChange={(e) => setLat(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Longitude" hint="For example 90.4125.">
                <input
                    inputMode="decimal"
                    value={lng}
                    onChange={(e) => setLng(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Site contact">
                <input
                    value={contactName}
                    onChange={(e) => setContactName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Contact phone">
                <input
                    type="tel"
                    value={contactPhone}
                    onChange={(e) => setContactPhone(e.target.value)}
                    maxLength={30}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Access notes"
                hint="Gate times, road limits, where trucks wait."
                className="sm:col-span-2"
            >
                <textarea
                    value={accessNotes}
                    onChange={(e) => setAccessNotes(e.target.value)}
                    maxLength={1000}
                    rows={3}
                    className={textareaClass}
                />
            </Field>
        </FormFrame>
    );
}

const kmValid = (value: string) => {
    if (value.trim() === "") return true;
    return /^\d{1,5}(\.\d{1,2})?$/.test(value.trim());
};

// Km from each active plant to the site. Orders suggest it. Empty means no stored distance.
// Mounted fresh each time it opens, so it starts from the saved distances.
export function DistanceEditor({
    siteId,
    plants,
    distances,
    onDone,
}: {
    siteId: string;
    plants: PlantOption[];
    distances: SiteDistance[];
    onDone: () => void;
}) {
    const save = useCustomerSave("", `/api/sites/${siteId}/distances`);
    const [values, setValues] = useState<Record<string, string>>(() =>
        Object.fromEntries(
            distances.map((d) => [d.plantId, String(d.distanceKm)]),
        ),
    );
    const activeIds = new Set(plants.map((p) => p.id));
    const dropped = distances.filter((d) => !activeIds.has(d.plantId));
    const allValid = plants.every((p) => kmValid(values[p.id] ?? ""));

    const body = {
        distances: plants
            .filter((p) => (values[p.id] ?? "").trim() !== "")
            .map((p) => ({
                plantId: p.id,
                distanceKm: Number(values[p.id]),
            })),
    };

    return (
        <div className="space-y-3 rounded-sm border p-3">
            {plants.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    There are no active plants yet.
                </p>
            )}
            <div className="grid gap-2 sm:grid-cols-2">
                {plants.map((plant) => (
                    <Field key={plant.id} label={`${plant.code} ${plant.name}`}>
                        <div className="flex items-center gap-2">
                            <input
                                inputMode="decimal"
                                value={values[plant.id] ?? ""}
                                onChange={(e) =>
                                    setValues((current) => ({
                                        ...current,
                                        [plant.id]: e.target.value,
                                    }))
                                }
                                aria-invalid={!kmValid(values[plant.id] ?? "")}
                                className={cn(
                                    inputClass,
                                    "h-11",
                                    !kmValid(values[plant.id] ?? "") &&
                                        "border-late",
                                )}
                            />
                            <span className="text-sm text-muted-foreground">
                                km
                            </span>
                        </div>
                    </Field>
                ))}
            </div>
            {dropped.length > 0 && (
                <p className="text-xs text-muted-foreground">
                    Saving removes the distance to inactive plant
                    {dropped.length === 1 ? "" : "s"}{" "}
                    {dropped.map((d) => d.plantCode).join(", ")}.
                </p>
            )}
            {!allValid && (
                <p className="text-sm text-late" role="alert">
                    Distances are numbers in km with up to 2 decimals.
                </p>
            )}
            {save.isError && (
                <p className="text-sm text-late" role="alert">
                    {save.error.message}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    className={cn("h-11 px-5", nextButtonClass)}
                    disabled={!allValid || save.isPending}
                    onClick={() => save.mutate(body, { onSuccess: onDone })}
                >
                    {save.isPending ? "Saving…" : "Save distances"}
                </Button>
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={onDone}
                >
                    Cancel
                </Button>
            </div>
        </div>
    );
}

export function SalesRepForm({
    rep,
    reps,
    users,
    onDone,
    onCancel,
}: {
    rep: SalesRep | null;
    reps: SalesRep[];
    users: { id: string; label: string; linkedRepId: string | null }[];
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useCustomerSave(
        "/api/sales-reps",
        rep ? `/api/sales-reps/${rep.id}` : null,
    );
    const [name, setName] = useState(rep?.name ?? "");
    const [region, setRegion] = useState(rep?.region ?? "");
    const [userId, setUserId] = useState(rep?.userId ?? "");
    const [managerId, setManagerId] = useState(rep?.managerId ?? "");

    const body = {
        name: name.trim(),
        region: region.trim() || null,
        userId: userId || null,
        managerId: managerId || null,
        status: rep?.status ?? "Active",
    };
    // A login can link to one rep only, so logins linked elsewhere are left out.
    const userOptions = users.filter(
        (u) => u.linkedRepId === null || u.linkedRepId === rep?.id,
    );

    return (
        <FormFrame
            title={rep ? "Edit sales rep" : "New sales rep"}
            saveLabel="Save sales rep"
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
            <Field label="Region">
                <input
                    value={region}
                    onChange={(e) => setRegion(e.target.value)}
                    maxLength={100}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Login"
                hint="Link the person's PlantFlow login. A rep needs one to add customers."
            >
                <select
                    value={userId}
                    onChange={(e) => setUserId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">No login</option>
                    {rep?.userId &&
                        !userOptions.some((u) => u.id === rep.userId) && (
                            <option value={rep.userId}>
                                {rep.userEmail ?? "Current login"}
                            </option>
                        )}
                    {userOptions.map((u) => (
                        <option key={u.id} value={u.id}>
                            {u.label}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Reports to">
                <select
                    value={managerId}
                    onChange={(e) => setManagerId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">Nobody</option>
                    {reps
                        .filter((r) => r.id !== rep?.id)
                        .map((r) => (
                            <option key={r.id} value={r.id}>
                                {r.name}
                                {r.status === "Inactive" ? " (inactive)" : ""}
                            </option>
                        ))}
                </select>
            </Field>
        </FormFrame>
    );
}
