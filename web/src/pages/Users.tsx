import { useState } from "react";
import { LuChevronDown, LuSearch } from "react-icons/lu";
import { useMe } from "@/auth/useMe";
import {
    inputClass,
    panelClass,
    tabClass,
    tabRowClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { JoinCodeCard } from "@/components/users/JoinCodeCard";
import { UserPanel } from "@/components/users/UserPanel";
import { useUsers } from "@/components/users/useUsers";
import { initialsOf } from "@/lib/format";
import type { UserStatus } from "@/lib/types";
import { statusLabel, type UserSummary } from "@/lib/users";
import { cn } from "@/lib/utils";

const tabs: { status: UserStatus; label: string }[] = [
    { status: "Pending", label: "Waiting" },
    { status: "Active", label: "Active" },
    { status: "Disabled", label: "Disabled" },
    { status: "Rejected", label: "Rejected" },
];

// Everyone in the company: approve sign-ups, give roles and plants, adjust single permissions,
// and disable people who leave.
export function Users() {
    const me = useMe().data!;
    const users = useUsers();
    const [chosenTab, setChosenTab] = useState<UserStatus | null>(null);
    const [search, setSearch] = useState("");
    const [open, setOpen] = useState<string | null>(null);

    const all = users.data ?? [];
    const count = (status: UserStatus) =>
        all.filter((u) => u.status === status).length;
    // Until someone picks a tab, open on the waiting sign-ups when there are any.
    const tab = chosenTab ?? (count("Pending") > 0 ? "Pending" : "Active");
    const needle = search.trim().toLowerCase();
    const shown = all.filter(
        (u) =>
            u.status === tab &&
            (!needle ||
                u.email.toLowerCase().includes(needle) ||
                (u.displayName ?? "").toLowerCase().includes(needle)),
    );

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Users</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    Approve people who join with the code, give them roles and
                    plants, and disable accounts when someone leaves. The
                    company always keeps at least one active Admin.
                </p>
            </header>

            <JoinCodeCard />

            <div role="tablist" aria-label="Users" className={tabRowClass}>
                {tabs.map((t) => (
                    <button
                        key={t.status}
                        type="button"
                        role="tab"
                        aria-selected={tab === t.status}
                        onClick={() => {
                            setChosenTab(t.status);
                            setOpen(null);
                        }}
                        className={tabClass(tab === t.status)}
                    >
                        {t.label}
                        <span className="tabular-nums opacity-70">
                            {count(t.status)}
                        </span>
                    </button>
                ))}
            </div>

            <label className="relative block sm:max-w-md">
                <LuSearch
                    aria-hidden
                    className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                />
                <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search name or email"
                    aria-label="Search users"
                    className={cn(inputClass, "h-11 pl-9")}
                />
            </label>

            {users.isLoading && <Skeleton className="h-40 w-full" />}
            {users.isError && (
                <p role="alert" className="text-sm text-late">
                    {users.error.message}
                </p>
            )}
            {users.data && shown.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    {needle
                        ? "Nobody here matches that search."
                        : tab === "Pending"
                          ? "Nobody is waiting for approval."
                          : `No ${statusLabel[tab].toLowerCase()} users.`}
                </p>
            )}

            {shown.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {shown.map((user) => (
                        <UserRow
                            key={user.id}
                            user={user}
                            isMe={user.id === me.id}
                            open={open === user.id}
                            onToggle={() =>
                                setOpen((now) =>
                                    now === user.id ? null : user.id,
                                )
                            }
                        />
                    ))}
                </ul>
            )}
        </div>
    );
}

function UserRow({
    user,
    isMe,
    open,
    onToggle,
}: {
    user: UserSummary;
    isMe: boolean;
    open: boolean;
    onToggle: () => void;
}) {
    const name = user.displayName ?? user.email;
    const panelId = `user-${user.id}`;

    return (
        <li>
            <button
                type="button"
                onClick={onToggle}
                disabled={isMe}
                aria-expanded={open}
                aria-controls={panelId}
                className="flex w-full items-center gap-4 px-4 py-3 text-left enabled:hover:bg-muted/40 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ink"
            >
                <span className="grid size-9 shrink-0 place-items-center rounded-full bg-muted text-xs font-bold">
                    {initialsOf(name)}
                </span>
                <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">
                        {name}
                        {isMe && (
                            <span className="font-normal text-muted-foreground">
                                {" "}
                                (you)
                            </span>
                        )}
                    </span>
                    {user.displayName && (
                        <span className="block truncate text-sm text-muted-foreground">
                            {user.email}
                        </span>
                    )}
                </span>
                <span className="hidden min-w-0 flex-[1.4] flex-wrap gap-1.5 md:flex">
                    {user.roles.length === 0 ? (
                        <span className="text-sm text-muted-foreground">
                            {user.status === "Active" ? "No role yet" : ""}
                        </span>
                    ) : (
                        user.roles.map((role) => (
                            <span
                                key={role}
                                className="rounded-sm border bg-card px-2 py-0.5 text-xs font-medium"
                            >
                                {role}
                            </span>
                        ))
                    )}
                </span>
                <span className="hidden w-28 shrink-0 truncate text-sm text-muted-foreground lg:block">
                    {user.status !== "Active"
                        ? ""
                        : user.plants.length === 0
                          ? "No plants"
                          : user.plants.map((p) => p.code).join(", ")}
                </span>
                {isMe ? (
                    <span className="w-6 shrink-0" />
                ) : (
                    <LuChevronDown
                        aria-hidden
                        className={cn(
                            "size-5 shrink-0 text-muted-foreground transition-transform motion-reduce:transition-none",
                            open && "rotate-180",
                        )}
                    />
                )}
            </button>
            {open && (
                <div id={panelId} className="border-t bg-muted/20 p-4">
                    <UserPanel user={user} />
                </div>
            )}
        </li>
    );
}
