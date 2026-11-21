import { useState, type ReactNode } from "react";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { inputClass, nextButtonClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import type { PlantOption } from "@/lib/orders";
import { groupPermissions, permissionLabel } from "@/lib/permissionLabels";
import type {
    PermissionOverride,
    RoleOption,
    UserDetail,
    UserSummary,
} from "@/lib/users";
import { cn } from "@/lib/utils";
import {
    useActivePlants,
    usePermissionKeys,
    useRoles,
    useSetOverrides,
    useSetRoles,
    useSetUserPlants,
    useStatusAction,
    useUserDetail,
} from "./useUsers";

// Everything an Admin can change about one person. Each part saves on its own.
export function UserPanel({ user }: { user: UserSummary }) {
    const detail = useUserDetail(user.id);
    const roles = useRoles();

    if (detail.isLoading || roles.isLoading) {
        return <Skeleton className="h-48 w-full" />;
    }
    const error = detail.error ?? roles.error;
    if (error || !detail.data || !roles.data) {
        return (
            <p role="alert" className="text-sm text-late">
                {error?.message ?? "This user could not be loaded."}
            </p>
        );
    }

    return (
        <div className="space-y-3">
            <AccountPart user={user} />
            {user.status === "Active" && (
                <>
                    <RolesPart detail={detail.data} roles={roles.data} />
                    <PlantsPart user={user} />
                    <OverridesPart detail={detail.data} roles={roles.data} />
                </>
            )}
        </div>
    );
}

function Part({
    title,
    hint,
    children,
}: {
    title: string;
    hint: string;
    children: ReactNode;
}) {
    return (
        <section className="space-y-3 rounded-sm border bg-background/60 p-4">
            <div>
                <h3 className="font-semibold">{title}</h3>
                <p className="text-sm text-muted-foreground">{hint}</p>
            </div>
            {children}
        </section>
    );
}

function ErrorLine({ error }: { error: Error | null }) {
    return error ? (
        <p role="alert" className="text-sm font-medium text-late">
            {error.message}
        </p>
    ) : null;
}

// Approve or reject a sign-up; disable (with a reason) or enable an account.
function AccountPart({ user }: { user: UserSummary }) {
    const action = useStatusAction(user.id);
    const [disabling, setDisabling] = useState(false);
    const [reason, setReason] = useState("");

    if (user.status === "Pending") {
        return (
            <Part
                title="Sign-up"
                hint="This person signed in and asked to join. Approve them, then give them a role so they can open screens."
            >
                <div className="flex flex-wrap gap-2">
                    <Button
                        className={cn("h-11 px-5", nextButtonClass)}
                        disabled={action.isPending}
                        onClick={() => action.mutate({ action: "approve" })}
                    >
                        Approve
                    </Button>
                    <Button
                        variant="outline"
                        tone="danger"
                        className="h-11 px-5"
                        disabled={action.isPending}
                        onClick={() => action.mutate({ action: "reject" })}
                    >
                        Reject
                    </Button>
                </div>
                <ErrorLine error={action.error} />
            </Part>
        );
    }

    if (user.status === "Rejected") {
        return (
            <Part
                title="Sign-up"
                hint="This sign-up was rejected. Approve it if it was a mistake."
            >
                <Button
                    variant="outline"
                    className="h-11 px-5"
                    disabled={action.isPending}
                    onClick={() => action.mutate({ action: "approve" })}
                >
                    Approve after all
                </Button>
                <ErrorLine error={action.error} />
            </Part>
        );
    }

    if (user.status === "Disabled") {
        return (
            <Part
                title="Account"
                hint="This account is disabled and can't sign in. Its history stays."
            >
                <Button
                    variant="outline"
                    className="h-11 px-5"
                    disabled={action.isPending}
                    onClick={() => action.mutate({ action: "enable" })}
                >
                    Enable account
                </Button>
                <ErrorLine error={action.error} />
            </Part>
        );
    }

    return (
        <Part
            title="Account"
            hint="Disable an account when someone leaves. They lose access at once; their history stays."
        >
            {disabling ? (
                <form
                    className="space-y-3"
                    onSubmit={(e) => {
                        e.preventDefault();
                        if (reason.trim()) {
                            action.mutate({
                                action: "disable",
                                reason: reason.trim(),
                            });
                        }
                    }}
                >
                    <Field
                        label="Reason"
                        hint="Kept in the audit log, for example 'Left the company'."
                    >
                        <input
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            maxLength={500}
                            className={inputClass}
                            autoFocus
                        />
                    </Field>
                    <div className="flex flex-wrap gap-2">
                        <Button
                            type="submit"
                            tone="danger"
                            className="h-11 px-5"
                            disabled={!reason.trim() || action.isPending}
                        >
                            Disable account
                        </Button>
                        <Button
                            type="button"
                            variant="outline"
                            className="h-11 px-5"
                            onClick={() => {
                                action.reset();
                                setDisabling(false);
                            }}
                        >
                            Never mind
                        </Button>
                    </div>
                </form>
            ) : (
                <Button
                    variant="outline"
                    tone="danger"
                    className="h-11 px-5"
                    onClick={() => setDisabling(true)}
                >
                    Disable account
                </Button>
            )}
            <ErrorLine error={action.error} />
        </Part>
    );
}

function SaveRow({
    dirty,
    pending,
    label,
    onSave,
    saved,
}: {
    dirty: boolean;
    pending: boolean;
    label: string;
    onSave: () => void;
    saved: boolean;
}) {
    return (
        <div className="flex flex-wrap items-center gap-3">
            <Button
                className={cn("h-11 px-5", dirty && nextButtonClass)}
                variant={dirty ? "default" : "outline"}
                disabled={!dirty || pending}
                onClick={onSave}
            >
                {pending ? "Saving…" : label}
            </Button>
            {saved && !dirty && (
                <span className="text-sm text-ok" role="status">
                    Saved.
                </span>
            )}
        </div>
    );
}

const sameSet = (a: string[], b: string[]) =>
    a.length === b.length && a.every((x) => b.includes(x));

function RolesPart({
    detail,
    roles,
}: {
    detail: UserDetail;
    roles: RoleOption[];
}) {
    const save = useSetRoles(detail.id);
    const [selected, setSelected] = useState(detail.roleIds);
    const dirty = !sameSet(selected, detail.roleIds);
    const toggle = (id: string) =>
        setSelected((current) =>
            current.includes(id)
                ? current.filter((x) => x !== id)
                : [...current, id],
        );

    return (
        <Part
            title="Roles"
            hint="What this person does. Each role opens the screens and actions that job needs."
        >
            {selected.length === 0 && (
                <p className="text-sm font-medium text-late">
                    No role yet, so this person can't open anything.
                </p>
            )}
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {roles.map((role) => (
                    <label
                        key={role.id}
                        className="flex min-h-11 cursor-pointer items-center gap-3 rounded-sm border bg-card px-3 py-2 text-sm hover:border-ink"
                    >
                        <input
                            type="checkbox"
                            className="size-4"
                            checked={selected.includes(role.id)}
                            onChange={() => toggle(role.id)}
                        />
                        <span>
                            <span className="font-medium">{role.name}</span>
                            <span className="block text-xs text-muted-foreground">
                                {role.permissions.length === 1
                                    ? "1 permission"
                                    : `${role.permissions.length} permissions`}
                            </span>
                        </span>
                    </label>
                ))}
            </div>
            <ErrorLine error={save.error} />
            <SaveRow
                dirty={dirty}
                pending={save.isPending}
                label="Save roles"
                saved={save.isSuccess}
                onSave={() => save.mutate(selected)}
            />
        </Part>
    );
}

function PlantsPart({ user }: { user: UserSummary }) {
    const plants = useActivePlants();
    const save = useSetUserPlants(user.id);
    const current = user.plants.map((p) => p.id);
    const [selected, setSelected] = useState(current);
    const dirty = !sameSet(selected, current);

    // Active plants, plus any assigned plant that has since been deactivated so it can be removed.
    const active: PlantOption[] = plants.data?.items ?? [];
    const activeIds = new Set(active.map((p) => p.id));
    const options = [
        ...active.map((p) => ({ ...p, inactive: false })),
        ...user.plants
            .filter((p) => !activeIds.has(p.id))
            .map((p) => ({ ...p, inactive: true })),
    ];
    const toggle = (id: string) =>
        setSelected((now) =>
            now.includes(id) ? now.filter((x) => x !== id) : [...now, id],
        );

    return (
        <Part
            title="Plants"
            hint="The plants this person works for. Plant-level decisions, such as approving a site survey, are limited to these."
        >
            {plants.isLoading && <Skeleton className="h-16 w-full" />}
            <ErrorLine error={plants.error} />
            {!plants.isLoading && options.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    There are no active plants. Add a plant first.
                </p>
            )}
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {options.map((plant) => (
                    <label
                        key={plant.id}
                        className="flex min-h-11 cursor-pointer items-center gap-3 rounded-sm border bg-card px-3 py-2 text-sm hover:border-ink"
                    >
                        <input
                            type="checkbox"
                            className="size-4"
                            checked={selected.includes(plant.id)}
                            onChange={() => toggle(plant.id)}
                        />
                        <span>
                            <span className="font-semibold">{plant.code}</span>{" "}
                            {plant.name}
                            {plant.inactive && (
                                <span className="text-muted-foreground">
                                    {" "}
                                    (inactive)
                                </span>
                            )}
                        </span>
                    </label>
                ))}
            </div>
            <ErrorLine error={save.error} />
            <SaveRow
                dirty={dirty}
                pending={save.isPending}
                label="Save plants"
                saved={save.isSuccess}
                onSave={() => save.mutate(selected)}
            />
        </Part>
    );
}

type Choice = "role" | "allow" | "deny";

// Grants and denies for this one person, on top of their roles. A deny beats any role.
function OverridesPart({
    detail,
    roles,
}: {
    detail: UserDetail;
    roles: RoleOption[];
}) {
    const keys = usePermissionKeys();
    const save = useSetOverrides(detail.id);
    const [open, setOpen] = useState(detail.permissionOverrides.length > 0);
    const initial = Object.fromEntries(
        detail.permissionOverrides.map((o) => [
            o.permissionKey,
            o.isGranted ? "allow" : "deny",
        ]),
    ) as Record<string, Choice>;
    const [choices, setChoices] = useState(initial);
    const fromRoles = new Set(
        roles
            .filter((r) => detail.roleIds.includes(r.id))
            .flatMap((r) => r.permissions),
    );
    const overrides: PermissionOverride[] = Object.entries(choices)
        .filter(([, c]) => c !== "role")
        .map(([permissionKey, c]) => ({
            permissionKey,
            isGranted: c === "allow",
        }));
    const dirty =
        JSON.stringify(
            overrides.map((o) => [o.permissionKey, o.isGranted]).sort(),
        ) !==
        JSON.stringify(
            detail.permissionOverrides
                .map((o) => [o.permissionKey, o.isGranted])
                .sort(),
        );

    return (
        <Part
            title="Adjustments"
            hint="Allow or deny a single permission for this person only, on top of their roles. A deny wins over every role."
        >
            {!open ? (
                <div className="flex flex-wrap items-center gap-3">
                    <span className="text-sm text-muted-foreground">
                        No adjustments. This person has exactly what their roles
                        give.
                    </span>
                    <Button
                        variant="outline"
                        className="h-11 px-4"
                        onClick={() => setOpen(true)}
                    >
                        Adjust permissions
                    </Button>
                </div>
            ) : (
                <>
                    {keys.isLoading && <Skeleton className="h-40 w-full" />}
                    <ErrorLine error={keys.error} />
                    <div className="space-y-4">
                        {groupPermissions(keys.data ?? []).map((group) => (
                            <fieldset key={group.area} className="space-y-1">
                                <legend className="mb-1 text-sm font-semibold">
                                    {group.area}
                                </legend>
                                <ul className="divide-y rounded-sm border bg-card">
                                    {group.keys.map((key) => {
                                        const choice = choices[key] ?? "role";
                                        return (
                                            <li
                                                key={key}
                                                className="flex flex-wrap items-center justify-between gap-2 px-3 py-2"
                                            >
                                                <span className="min-w-0 text-sm">
                                                    {permissionLabel(key)}
                                                    <span className="block text-xs text-muted-foreground">
                                                        {fromRoles.has(key)
                                                            ? "Given by a role"
                                                            : "Not given by their roles"}
                                                    </span>
                                                </span>
                                                <select
                                                    aria-label={permissionLabel(
                                                        key,
                                                    )}
                                                    value={choice}
                                                    onChange={(e) =>
                                                        setChoices((now) => ({
                                                            ...now,
                                                            [key]: e.target
                                                                .value as Choice,
                                                        }))
                                                    }
                                                    className={cn(
                                                        inputClass,
                                                        "w-44",
                                                        choice === "allow" &&
                                                            "border-ok font-medium text-ok",
                                                        choice === "deny" &&
                                                            "border-late font-medium text-late",
                                                    )}
                                                >
                                                    <option value="role">
                                                        As their roles say
                                                    </option>
                                                    <option value="allow">
                                                        Allow
                                                    </option>
                                                    <option value="deny">
                                                        Deny
                                                    </option>
                                                </select>
                                            </li>
                                        );
                                    })}
                                </ul>
                            </fieldset>
                        ))}
                    </div>
                    <ErrorLine error={save.error} />
                    <SaveRow
                        dirty={dirty}
                        pending={save.isPending}
                        label="Save adjustments"
                        saved={save.isSuccess}
                        onSave={() => save.mutate(overrides)}
                    />
                </>
            )}
        </Part>
    );
}
