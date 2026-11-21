import { useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import {
    offsetLabel,
    offsets,
    reasonFields,
    surveyLabel,
    wholeIn,
    type SurveyRequirement,
    type TenantSettings,
    type VolumeUnit,
} from "@/lib/settings";
import { cn } from "@/lib/utils";

const settingsKey = ["settings", "tenant"];

// How the company works: billing terms, planning rules, return and ownership windows. Each section saves on its own.
export function Settings() {
    const settings = useQuery({
        queryKey: settingsKey,
        queryFn: () => api<TenantSettings>("/api/settings"),
    });

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Settings</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    How your company works in PlantFlow. Each section saves on
                    its own, and a change applies from the moment it is saved.
                </p>
            </header>

            {settings.isLoading && <Skeleton className="h-96 w-full" />}
            {settings.isError && (
                <p role="alert" className="text-sm text-late">
                    {settings.error.message}
                </p>
            )}
            {settings.data && (
                <div className="space-y-4">
                    <CompanySection settings={settings.data} />
                    <BillingSection settings={settings.data} />
                    <PlanningSection settings={settings.data} />
                    <ReturnsSection settings={settings.data} />
                    <SalesSection settings={settings.data} />
                </div>
            )}
        </div>
    );
}

// A panel with its own Save. Saving sends every setting, with this section's changes on top.
function Section({
    title,
    description,
    settings,
    draft,
    valid,
    children,
}: {
    title: string;
    description: string;
    settings: TenantSettings;
    draft: Partial<TenantSettings>;
    valid: boolean;
    children: ReactNode;
}) {
    const queryClient = useQueryClient();
    const [reason, setReason] = useState("");
    const changed = (Object.keys(draft) as (keyof TenantSettings)[]).filter(
        (key) => draft[key] !== settings[key],
    );
    const needsReason = changed.some((key) =>
        (reasonFields as readonly string[]).includes(key),
    );
    const save = useMutation({
        mutationFn: () =>
            api<unknown>("/api/settings", {
                method: "PUT",
                body: JSON.stringify({
                    ...settings,
                    ...draft,
                    reason: needsReason ? reason.trim() : null,
                }),
            }),
        onSuccess: async () => {
            setReason("");
            await queryClient.invalidateQueries({ queryKey: ["settings"] });
        },
    });
    const dirty = changed.length > 0;
    const canSave =
        dirty &&
        valid &&
        (!needsReason || reason.trim().length > 0) &&
        !save.isPending;

    return (
        <section
            className={cn(
                panelClass,
                "grid gap-5 p-5 lg:grid-cols-[17rem_1fr]",
            )}
            aria-labelledby={`${title}-title`}
        >
            <div>
                <h2 id={`${title}-title`} className="display text-2xl">
                    {title}
                </h2>
                <p className="mt-1 text-sm text-muted-foreground">
                    {description}
                </p>
            </div>
            <form
                className="space-y-4"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (canSave) save.mutate();
                }}
            >
                <div className="grid gap-4 sm:grid-cols-2">{children}</div>
                {needsReason && (
                    <Field
                        label="Reason for this change"
                        hint="Required for VAT, credit approval and the free pump threshold. It is kept in the audit log."
                    >
                        <input
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            maxLength={500}
                            className={inputClass}
                        />
                    </Field>
                )}
                {save.isError && (
                    <p role="alert" className="text-sm font-medium text-late">
                        {save.error.message}
                    </p>
                )}
                <div className="flex flex-wrap items-center gap-3">
                    <Button
                        type="submit"
                        className={cn("h-11 px-5", dirty && nextButtonClass)}
                        variant={dirty ? "default" : "outline"}
                        disabled={!canSave}
                    >
                        {save.isPending
                            ? "Saving…"
                            : `Save ${title.toLowerCase()}`}
                    </Button>
                    {save.isSuccess && !dirty && (
                        <span className="text-sm text-ok" role="status">
                            Saved.
                        </span>
                    )}
                </div>
            </form>
        </section>
    );
}

function Toggle({
    label,
    hint,
    checked,
    onChange,
}: {
    label: string;
    hint: string;
    checked: boolean;
    onChange: (value: boolean) => void;
}) {
    return (
        <label className="flex min-h-11 gap-3 text-sm sm:col-span-2">
            <input
                type="checkbox"
                checked={checked}
                onChange={(e) => onChange(e.target.checked)}
                className="mt-0.5 size-5 shrink-0"
            />
            <span>
                <span className="block font-medium">{label}</span>
                <span className="mt-0.5 block text-xs text-muted-foreground">
                    {hint}
                </span>
            </span>
        </label>
    );
}

function NumberField({
    label,
    hint,
    value,
    onChange,
    invalid,
}: {
    label: string;
    hint: string;
    value: string;
    onChange: (value: string) => void;
    invalid: boolean;
}) {
    return (
        <Field label={label} hint={hint}>
            <input
                inputMode="decimal"
                value={value}
                onChange={(e) => onChange(e.target.value)}
                aria-invalid={invalid}
                className={cn(inputClass, invalid && "border-late")}
            />
        </Field>
    );
}

function CompanySection({ settings }: { settings: TenantSettings }) {
    const [name, setName] = useState(settings.name);
    const [unit, setUnit] = useState<VolumeUnit>(settings.volumeUnit);
    const [offset, setOffset] = useState(settings.utcOffsetMinutes);
    const choices = offsets.includes(settings.utcOffsetMinutes)
        ? offsets
        : [...offsets, settings.utcOffsetMinutes].sort((a, b) => a - b);

    return (
        <Section
            title="Company"
            description="Your company's name as people see it, the unit volumes are shown in, and the plants' time zone."
            settings={settings}
            draft={{
                name: name.trim(),
                volumeUnit: unit,
                utcOffsetMinutes: offset,
            }}
            valid={name.trim().length >= 2}
        >
            <Field label="Company name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Show volumes in"
                hint="Only changes how volumes are shown. They are always stored in CFT."
            >
                <select
                    value={unit}
                    onChange={(e) => setUnit(e.target.value as VolumeUnit)}
                    className={inputClass}
                >
                    <option value="Cft">CFT</option>
                    <option value="M3">Cubic metres (m³)</option>
                </select>
            </Field>
            <Field
                label="Time zone"
                hint={
                    settings.timeZoneLocked
                        ? "Fixed now that the company has orders, because it decides which day each shift and pour falls on."
                        : "Shift and pour times are in this time zone. It is fixed once the first order is made."
                }
            >
                <select
                    value={offset}
                    onChange={(e) => setOffset(Number(e.target.value))}
                    disabled={settings.timeZoneLocked}
                    className={inputClass}
                >
                    {choices.map((o) => (
                        <option key={o} value={o}>
                            {offsetLabel(o)}
                            {o === 360 ? " (Dhaka)" : ""}
                        </option>
                    ))}
                </select>
            </Field>
        </Section>
    );
}

function BillingSection({ settings }: { settings: TenantSettings }) {
    const [vat, setVat] = useState(settings.vatOnConcrete);
    const [credit, setCredit] = useState(settings.requireCreditApproval);
    const [freeAbove, setFreeAbove] = useState(
        settings.pumpFreeAboveCft == null
            ? ""
            : String(settings.pumpFreeAboveCft),
    );
    const freeValue = freeAbove.trim() === "" ? null : Number(freeAbove);
    const freeValid =
        freeValue === null ||
        (Number.isFinite(freeValue) &&
            freeValue >= 0.01 &&
            freeValue <= 10_000_000 &&
            Math.round(freeValue * 100) === freeValue * 100);

    return (
        <Section
            title="Orders and billing"
            description="What customers pay on top of the concrete, and who must approve an order. Changing these needs a reason."
            settings={settings}
            draft={{
                vatOnConcrete: vat,
                requireCreditApproval: credit,
                pumpFreeAboveCft: freeValid ? freeValue : null,
            }}
            valid={freeValid}
        >
            <Toggle
                label="Charge VAT on concrete"
                hint="Turn off if the concrete itself is not taxed. Service charges follow their own VAT setting, and VAT-exempt customers pay none."
                checked={vat}
                onChange={setVat}
            />
            <Toggle
                label="Every order needs credit approval"
                hint="When on, every order also needs a Credit approval before it can be confirmed."
                checked={credit}
                onChange={setCredit}
            />
            <NumberField
                label="Pumping is free above (CFT)"
                hint="An order pumping more than this pays no pump charge. Leave empty to always charge."
                value={freeAbove}
                onChange={setFreeAbove}
                invalid={!freeValid}
            />
        </Section>
    );
}

function PlanningSection({ settings }: { settings: TenantSettings }) {
    const [survey, setSurvey] = useState<SurveyRequirement>(
        settings.requireSurveyBeforePlanning,
    );
    const [rest, setRest] = useState(String(settings.pumpRestShifts));
    const [speed, setSpeed] = useState(String(settings.averageSpeedKmh));
    const [assembly, setAssembly] = useState(
        String(settings.pumpAssemblyMinutes),
    );
    const [truck, setTruck] = useState(String(settings.truckLoadCft));
    const [late, setLate] = useState(String(settings.lateThresholdMinutes));
    const truckValue = Number(truck);
    const truckValid =
        truck.trim() !== "" &&
        Number.isFinite(truckValue) &&
        truckValue >= 1 &&
        truckValue <= 100_000;
    const checks = {
        rest: wholeIn(rest, 0, 3),
        speed: wholeIn(speed, 1, 120),
        assembly: wholeIn(assembly, 0, 480),
        late: wholeIn(late, 0, 1440),
    };

    return (
        <Section
            title="Planning and delivery"
            description="The rules and estimates the planning board and the live board use."
            settings={settings}
            draft={{
                requireSurveyBeforePlanning: survey,
                pumpRestShifts: checks.rest
                    ? Number(rest)
                    : settings.pumpRestShifts,
                averageSpeedKmh: checks.speed
                    ? Number(speed)
                    : settings.averageSpeedKmh,
                pumpAssemblyMinutes: checks.assembly
                    ? Number(assembly)
                    : settings.pumpAssemblyMinutes,
                truckLoadCft: truckValid ? truckValue : settings.truckLoadCft,
                lateThresholdMinutes: checks.late
                    ? Number(late)
                    : settings.lateThresholdMinutes,
            }}
            valid={Object.values(checks).every(Boolean) && truckValid}
        >
            <Field
                label="Site survey before planning"
                hint="Which orders need an approved site survey before their pours can be planned."
            >
                <select
                    value={survey}
                    onChange={(e) =>
                        setSurvey(e.target.value as SurveyRequirement)
                    }
                    className={inputClass}
                >
                    {(Object.keys(surveyLabel) as SurveyRequirement[]).map(
                        (s) => (
                            <option key={s} value={s}>
                                {surveyLabel[s]}
                            </option>
                        ),
                    )}
                </select>
            </Field>
            <NumberField
                label="Pump rest (shifts)"
                hint="A pump rests this many following shifts of the same day after a pour. 0 to 3; 0 turns the rule off."
                value={rest}
                onChange={setRest}
                invalid={!checks.rest}
            />
            <NumberField
                label="Average road speed (km/h)"
                hint="Used to work out when pumps and the first truck must leave. 1 to 120."
                value={speed}
                onChange={setSpeed}
                invalid={!checks.speed}
            />
            <NumberField
                label="Pump set-up time (minutes)"
                hint="How long a pump takes to set up on site, unless a pump says otherwise. 0 to 480."
                value={assembly}
                onChange={setAssembly}
                invalid={!checks.assembly}
            />
            <NumberField
                label="Truck load (CFT)"
                hint="A typical load, used to estimate how many trucks a pour needs."
                value={truck}
                onChange={setTruck}
                invalid={!truckValid}
            />
            <NumberField
                label="Late after (minutes)"
                hint="The live board flags a step this many minutes behind plan. 0 to 1440."
                value={late}
                onChange={setLate}
                invalid={!checks.late}
            />
        </Section>
    );
}

function ReturnsSection({ settings }: { settings: TenantSettings }) {
    const [minutes, setMinutes] = useState(
        String(settings.returnWindowMinutes),
    );
    const valid = wholeIn(minutes, 0, 1440);

    return (
        <Section
            title="Returns"
            description="Concrete that comes back after a pour."
            settings={settings}
            draft={{
                returnWindowMinutes: valid
                    ? Number(minutes)
                    : settings.returnWindowMinutes,
            }}
            valid={valid}
        >
            <NumberField
                label="Return window (minutes)"
                hint="For this long after pouring ends, a part return can be recorded without a plant manager. After that, a plant manager must record it. 0 to 1440."
                value={minutes}
                onChange={setMinutes}
                invalid={!valid}
            />
        </Section>
    );
}

function SalesSection({ settings }: { settings: TenantSettings }) {
    const [days, setDays] = useState(String(settings.customerOwnershipDays));
    const valid = wholeIn(days, 0, 3650);

    return (
        <Section
            title="Sales"
            description="How long a sales rep keeps a customer to themselves."
            settings={settings}
            draft={{
                customerOwnershipDays: valid
                    ? Number(days)
                    : settings.customerOwnershipDays,
            }}
            valid={valid}
        >
            <NumberField
                label="Customer opens up after (days)"
                hint="A customer with no confirmed order for this many days becomes open to every rep. 0 means a rep keeps their customers for good."
                value={days}
                onChange={setDays}
                invalid={!valid}
            />
        </Section>
    );
}
