import { useState } from "react";
import { LuCheck } from "react-icons/lu";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { nextButtonClass, panelClass } from "@/components/orders/styles";
import {
    useActivePlants,
    useSetUserPlants,
    useUsers,
} from "@/components/users/useUsers";
import { Skeleton } from "@/components/ui/skeleton";
import type { PlantOption } from "@/lib/orders";
import type { UserSummary } from "@/lib/users";
import { cn } from "@/lib/utils";

// Users and the plants they work for. Plant-scoped decisions, such as approving a
// site survey, are limited to these plants. The full user administration comes later.
export function Users() {
    const me = useMe().data!;
    const users = useUsers();
    const plants = useActivePlants();
    const [editing, setEditing] = useState<string | null>(null);
    const [savedFor, setSavedFor] = useState<string | null>(null);

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <header>
                <h1 className="display text-4xl leading-none">Users</h1>
                <p className="mt-2 text-sm text-muted-foreground">
                    Choose the plants each person works for. A plant manager can
                    only approve site surveys for their own plants.
                </p>
            </header>

            {users.isLoading && <Skeleton className="h-40 w-full" />}
            {users.isError && (
                <p role="alert" className="text-sm text-late">
                    {users.error.message}
                </p>
            )}
            {plants.isError && (
                <p role="alert" className="text-sm text-late">
                    {plants.error.message}
                </p>
            )}
            {users.data && users.data.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    No users in this company yet.
                </p>
            )}

            <ul className={cn(panelClass, "divide-y")}>
                {users.data?.map((user) => (
                    <li key={user.id} className="space-y-3 px-4 py-3">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                            <div className="min-w-0">
                                <span className="block truncate font-medium">
                                    {user.displayName ?? user.email}
                                </span>
                                {user.displayName && (
                                    <span className="block truncate text-sm text-muted-foreground">
                                        {user.email}
                                    </span>
                                )}
                                <span className="mt-1 block text-xs text-muted-foreground">
                                    {user.status}
                                    {user.roles.length > 0 &&
                                        ` · ${user.roles.join(", ")}`}
                                </span>
                            </div>
                            {user.id === me.id ? (
                                <span className="text-xs text-muted-foreground">
                                    You cannot change your own account.
                                </span>
                            ) : (
                                editing !== user.id && (
                                    <Button
                                        variant="outline"
                                        className="h-11"
                                        onClick={() => {
                                            setSavedFor(null);
                                            setEditing(user.id);
                                        }}
                                    >
                                        Change plants
                                    </Button>
                                )
                            )}
                        </div>

                        {editing === user.id ? (
                            <PlantPicker
                                user={user}
                                plants={plants.data?.items ?? []}
                                loading={plants.isLoading}
                                onDone={(saved) => {
                                    setEditing(null);
                                    if (saved) setSavedFor(user.id);
                                }}
                            />
                        ) : (
                            <PlantChips
                                user={user}
                                saved={savedFor === user.id}
                            />
                        )}
                    </li>
                ))}
            </ul>
        </div>
    );
}

function PlantChips({ user, saved }: { user: UserSummary; saved: boolean }) {
    return (
        <div className="flex flex-wrap items-center gap-2">
            {user.plants.length === 0 ? (
                <span className="text-sm text-muted-foreground">No plants</span>
            ) : (
                user.plants.map((plant) => (
                    <span
                        key={plant.id}
                        className="inline-flex items-center rounded-sm border px-2 py-0.5 text-xs font-semibold"
                        title={plant.name}
                    >
                        {plant.code}
                    </span>
                ))
            )}
            {saved && (
                <span className="inline-flex items-center gap-1 text-xs text-ok">
                    <LuCheck aria-hidden className="size-3.5" /> Saved
                </span>
            )}
        </div>
    );
}

// Mounted fresh each time it opens, so its selection starts from the user's current plants.
function PlantPicker({
    user,
    plants,
    loading,
    onDone,
}: {
    user: UserSummary;
    plants: PlantOption[];
    loading: boolean;
    onDone: (saved: boolean) => void;
}) {
    const [selected, setSelected] = useState(
        () => new Set(user.plants.map((plant) => plant.id)),
    );
    const save = useSetUserPlants();

    // Active plants, plus any assigned plant that has since been deactivated so it can be removed.
    const activeIds = new Set(plants.map((plant) => plant.id));
    const options = [
        ...plants.map((plant) => ({ ...plant, inactive: false })),
        ...user.plants
            .filter((plant) => !activeIds.has(plant.id))
            .map((plant) => ({ ...plant, inactive: true })),
    ];

    const toggle = (id: string) =>
        setSelected((current) => {
            const next = new Set(current);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });

    return (
        <div className="space-y-3">
            {loading && <Skeleton className="h-24 w-full" />}
            {!loading && options.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    There are no active plants. Add a plant first.
                </p>
            )}
            <div className="grid gap-2 sm:grid-cols-2">
                {options.map((plant) => (
                    <label
                        key={plant.id}
                        className="flex min-h-11 cursor-pointer items-center gap-3 rounded-sm border bg-card px-3 py-2 text-sm hover:border-ink"
                    >
                        <input
                            type="checkbox"
                            className="size-4"
                            checked={selected.has(plant.id)}
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

            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}

            <div className="flex flex-wrap gap-2">
                <Button
                    className={cn("h-11", nextButtonClass)}
                    disabled={save.isPending}
                    onClick={() =>
                        save.mutate(
                            { userId: user.id, plantIds: [...selected] },
                            { onSuccess: () => onDone(true) },
                        )
                    }
                >
                    {save.isPending ? "Saving…" : "Save plants"}
                </Button>
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={() => onDone(false)}
                >
                    Cancel
                </Button>
            </div>
        </div>
    );
}
