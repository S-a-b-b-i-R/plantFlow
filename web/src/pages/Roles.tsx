import { useState } from "react";
import { LuLock, LuPlus } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import {
    useCreateRole,
    useDeleteRole,
    useUpdateRole,
} from "@/components/roles/useRoleMutations";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermissionKeys, useRoles } from "@/components/users/useUsers";
import { groupPermissions, permissionLabel } from "@/lib/permissionLabels";
import type { RoleOption } from "@/lib/users";
import { cn } from "@/lib/utils";

const NEW = "new";

const peopleLabel = (n: number) =>
    n === 0 ? "Nobody" : n === 1 ? "1 person" : `${n} people`;

// What each role can do. Built-in roles can be renamed and changed, a company can add its own,
// and the Admin role is locked with every permission.
export function Roles() {
    const roles = useRoles();
    const keys = usePermissionKeys();
    const [chosen, setChosen] = useState<string | null>(null);

    const list = roles.data ?? [];
    const selectedId =
        chosen === NEW
            ? NEW
            : (list.find((r) => r.id === chosen)?.id ??
              list.find((r) => !r.isLocked)?.id ??
              list[0]?.id ??
              null);
    const selected = list.find((r) => r.id === selectedId);

    return (
        <div className="space-y-5">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">Roles</h1>
                    <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                        A role is a job, such as Planner or Accounts. Change
                        what a role can do and everyone who has it gets the
                        change straight away. Give roles to people on the Users
                        screen.
                    </p>
                </div>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={() => setChosen(NEW)}
                >
                    <LuPlus aria-hidden className="size-4" />
                    New role
                </Button>
            </header>

            {(roles.isLoading || keys.isLoading) && (
                <Skeleton className="h-64 w-full" />
            )}
            {(roles.error ?? keys.error) && (
                <p role="alert" className="text-sm text-late">
                    {(roles.error ?? keys.error)?.message}
                </p>
            )}

            {roles.data && keys.data && (
                <div className="grid items-start gap-4 lg:grid-cols-[18rem_1fr]">
                    <nav
                        aria-label="Roles"
                        className={cn(
                            panelClass,
                            "divide-y lg:sticky lg:top-4",
                        )}
                    >
                        {list.map((role) => (
                            <button
                                key={role.id}
                                type="button"
                                aria-current={
                                    role.id === selectedId ? "true" : undefined
                                }
                                onClick={() => setChosen(role.id)}
                                className={cn(
                                    "flex min-h-14 w-full items-center gap-3 px-4 py-2 text-left hover:bg-muted/40 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ink",
                                    role.id === selectedId &&
                                        "bg-muted/60 shadow-[inset_3px_0_0_var(--color-signal)]",
                                )}
                            >
                                <span className="min-w-0 flex-1">
                                    <span className="block truncate font-medium">
                                        {role.name}
                                    </span>
                                    <span className="block text-xs text-muted-foreground">
                                        {role.isBuiltIn ? "Built-in" : "Custom"}
                                        {", "}
                                        {peopleLabel(
                                            role.userCount,
                                        ).toLowerCase()}
                                    </span>
                                </span>
                                {role.isLocked && (
                                    <LuLock
                                        aria-label="Locked"
                                        className="size-4 shrink-0 text-muted-foreground"
                                    />
                                )}
                            </button>
                        ))}
                        {selectedId === NEW && (
                            <div className="flex min-h-14 items-center bg-muted/60 px-4 font-medium shadow-[inset_3px_0_0_var(--color-signal)]">
                                New role
                            </div>
                        )}
                    </nav>

                    {selectedId === NEW ? (
                        <NewRole
                            allKeys={keys.data}
                            onDone={(id) => setChosen(id)}
                            onCancel={() => setChosen(null)}
                        />
                    ) : selected ? (
                        <RoleEditor
                            key={selected.id}
                            role={selected}
                            allKeys={keys.data}
                            onDeleted={() => setChosen(null)}
                        />
                    ) : null}
                </div>
            )}
        </div>
    );
}

function ErrorLine({ error }: { error: Error | null }) {
    return error ? (
        <p role="alert" className="text-sm font-medium text-late">
            {error.message}
        </p>
    ) : null;
}

// Permissions grouped by area, with a count per area. Read-only for the locked Admin role.
function PermissionChecklist({
    allKeys,
    selected,
    onChange,
    locked = false,
}: {
    allKeys: string[];
    selected: string[];
    onChange: (keys: string[]) => void;
    locked?: boolean;
}) {
    const toggle = (key: string) =>
        onChange(
            selected.includes(key)
                ? selected.filter((k) => k !== key)
                : [...selected, key],
        );

    return (
        <div className="space-y-4">
            {groupPermissions(allKeys).map((group) => {
                const on = group.keys.filter((k) => selected.includes(k));
                const all = on.length === group.keys.length;
                return (
                    <fieldset key={group.area} disabled={locked}>
                        <div className="mb-1 flex items-center justify-between gap-2">
                            <legend className="text-sm font-semibold">
                                {group.area}
                                <span className="ml-2 font-normal text-muted-foreground tabular-nums">
                                    {on.length} of {group.keys.length}
                                </span>
                            </legend>
                            {!locked && (
                                <button
                                    type="button"
                                    className="min-h-9 px-2 text-sm font-medium underline-offset-2 hover:underline"
                                    onClick={() =>
                                        onChange(
                                            all
                                                ? selected.filter(
                                                      (k) =>
                                                          !group.keys.includes(
                                                              k,
                                                          ),
                                                  )
                                                : [
                                                      ...selected,
                                                      ...group.keys.filter(
                                                          (k) =>
                                                              !selected.includes(
                                                                  k,
                                                              ),
                                                      ),
                                                  ],
                                        )
                                    }
                                >
                                    {all ? "Clear all" : "Select all"}
                                </button>
                            )}
                        </div>
                        <div className="grid overflow-hidden rounded-sm border bg-card sm:grid-cols-2">
                            {group.keys.map((key) => (
                                <label
                                    key={key}
                                    className={cn(
                                        "flex min-h-11 items-center gap-3 border-t px-3 py-2 text-sm first:border-t-0 sm:odd:border-r sm:[&:nth-child(2)]:border-t-0",
                                        !locked &&
                                            "cursor-pointer hover:bg-muted/40",
                                    )}
                                >
                                    <input
                                        type="checkbox"
                                        className="size-4"
                                        checked={selected.includes(key)}
                                        onChange={() => toggle(key)}
                                    />
                                    {permissionLabel(key)}
                                </label>
                            ))}
                        </div>
                    </fieldset>
                );
            })}
        </div>
    );
}

const sameSet = (a: string[], b: string[]) =>
    a.length === b.length && a.every((x) => b.includes(x));

function RoleEditor({
    role,
    allKeys,
    onDeleted,
}: {
    role: RoleOption;
    allKeys: string[];
    onDeleted: () => void;
}) {
    const update = useUpdateRole(role.id);
    const remove = useDeleteRole(role.id);
    const [name, setName] = useState(role.name);
    const [permissions, setPermissions] = useState(role.permissions);
    const [confirmDelete, setConfirmDelete] = useState(false);
    const dirty =
        name.trim() !== role.name || !sameSet(permissions, role.permissions);

    if (role.isLocked) {
        return (
            <section className={cn(panelClass, "space-y-4 p-4 sm:p-5")}>
                <div>
                    <h2 className="flex items-center gap-2 text-xl font-semibold">
                        <LuLock aria-hidden className="size-5" />
                        {role.name}
                    </h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                        Admin always has every permission, including new ones
                        PlantFlow adds, so the company can never lock itself
                        out. It can't be renamed or changed.{" "}
                        {peopleLabel(role.userCount)}{" "}
                        {role.userCount === 1 ? "has" : "have"} it.
                    </p>
                </div>
                <PermissionChecklist
                    allKeys={allKeys}
                    selected={role.permissions}
                    onChange={() => {}}
                    locked
                />
            </section>
        );
    }

    return (
        <form
            className={cn(panelClass, "space-y-5 p-4 sm:p-5")}
            onSubmit={(e) => {
                e.preventDefault();
                if (dirty && name.trim().length >= 2) {
                    update.mutate({ name: name.trim(), permissions });
                }
            }}
        >
            <div className="space-y-1">
                <p className="text-sm text-muted-foreground">
                    {role.isBuiltIn
                        ? "One of PlantFlow's roles. You can rename it and change what it does, but not delete it."
                        : "Your company's own role."}{" "}
                    {peopleLabel(role.userCount)}{" "}
                    {role.userCount === 1 ? "has" : "have"} it.
                </p>
            </div>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={100}
                    className={cn(inputClass, "sm:max-w-md")}
                />
            </Field>
            <PermissionChecklist
                allKeys={allKeys}
                selected={permissions}
                onChange={setPermissions}
            />
            <ErrorLine error={update.error ?? remove.error} />
            <div className="flex flex-wrap items-center gap-3 border-t pt-4">
                <Button
                    type="submit"
                    className={cn("h-11 px-5", dirty && nextButtonClass)}
                    variant={dirty ? "default" : "outline"}
                    disabled={
                        !dirty || name.trim().length < 2 || update.isPending
                    }
                >
                    {update.isPending ? "Saving…" : "Save role"}
                </Button>
                {update.isSuccess && !dirty && (
                    <span role="status" className="text-sm text-ok">
                        Saved.
                    </span>
                )}
                {!role.isBuiltIn && (
                    <span className="ml-auto flex flex-wrap items-center gap-2">
                        {confirmDelete ? (
                            <>
                                <span className="text-sm">
                                    Delete {role.name}?
                                </span>
                                <Button
                                    type="button"
                                    tone="danger"
                                    className="h-11 px-4"
                                    disabled={remove.isPending}
                                    onClick={() =>
                                        remove.mutate(undefined, {
                                            onSuccess: onDeleted,
                                        })
                                    }
                                >
                                    Delete role
                                </Button>
                                <Button
                                    type="button"
                                    variant="outline"
                                    className="h-11 px-4"
                                    onClick={() => {
                                        remove.reset();
                                        setConfirmDelete(false);
                                    }}
                                >
                                    Keep it
                                </Button>
                            </>
                        ) : (
                            <Button
                                type="button"
                                variant="outline"
                                tone="danger"
                                className="h-11 px-4"
                                onClick={() => setConfirmDelete(true)}
                            >
                                Delete role
                            </Button>
                        )}
                    </span>
                )}
            </div>
        </form>
    );
}

function NewRole({
    allKeys,
    onDone,
    onCancel,
}: {
    allKeys: string[];
    onDone: (id: string) => void;
    onCancel: () => void;
}) {
    const create = useCreateRole();
    const [name, setName] = useState("");
    const [permissions, setPermissions] = useState<string[]>([]);
    const ready = name.trim().length >= 2;

    return (
        <form
            className={cn(panelClass, "space-y-5 p-4 sm:p-5")}
            onSubmit={(e) => {
                e.preventDefault();
                if (ready) {
                    create.mutate(
                        { name: name.trim(), permissions },
                        { onSuccess: (role) => onDone(role.id) },
                    );
                }
            }}
        >
            <p className="text-sm text-muted-foreground">
                For a job PlantFlow's roles don't cover. Pick what it can do;
                you can change it later.
            </p>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={100}
                    placeholder="For example Yard Supervisor"
                    className={cn(inputClass, "sm:max-w-md")}
                    autoFocus
                />
            </Field>
            <PermissionChecklist
                allKeys={allKeys}
                selected={permissions}
                onChange={setPermissions}
            />
            <ErrorLine error={create.error} />
            <div className="flex flex-wrap gap-3 border-t pt-4">
                <Button
                    type="submit"
                    className={cn("h-11 px-5", ready && nextButtonClass)}
                    variant={ready ? "default" : "outline"}
                    disabled={!ready || create.isPending}
                >
                    {create.isPending ? "Creating…" : "Create role"}
                </Button>
                <Button
                    type="button"
                    variant="outline"
                    className="h-11 px-5"
                    onClick={onCancel}
                >
                    Cancel
                </Button>
            </div>
        </form>
    );
}
