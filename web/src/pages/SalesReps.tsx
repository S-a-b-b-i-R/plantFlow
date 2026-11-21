import { useState } from "react";
import { LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { SalesRepForm } from "@/components/customers/CustomerForms";
import {
    useCustomerSave,
    useLinkableUsers,
    useSalesReps,
} from "@/components/customers/useCustomers";
import { OpButton as Button } from "@/components/operator/OpButton";
import { nextButtonClass, panelClass } from "@/components/orders/styles";
import { StatusChip } from "@/components/plants/StatusChip";
import { Skeleton } from "@/components/ui/skeleton";
import type { SalesRep } from "@/lib/customers";
import { cn } from "@/lib/utils";

// The sales team. A rep linked to a login can add customers, who then belong to that rep.
export function SalesReps() {
    const me = useMe().data!;
    const canAssign = can(me, PERMISSIONS.customersAssign);
    const reps = useSalesReps();
    const users = useLinkableUsers(canAssign);
    const [editing, setEditing] = useState<SalesRep | "new" | null>(null);
    const all = reps.data?.items ?? [];
    const byId = new Map(all.map((r) => [r.id, r]));
    const userOptions = (users.data ?? []).map((u) => ({
        id: u.id,
        label: u.displayName ? `${u.displayName} (${u.email})` : u.email,
        linkedRepId: u.linkedRepId,
    }));
    const close = () => setEditing(null);

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">
                        Sales reps
                    </h1>
                    <p className="mt-2 text-sm text-muted-foreground">
                        Link each rep to their login so they can add their own
                        customers.
                    </p>
                </div>
                {canAssign && editing === null && (
                    <Button
                        className={cn("h-11 px-4", nextButtonClass)}
                        onClick={() => setEditing("new")}
                    >
                        <LuPlus aria-hidden /> New sales rep
                    </Button>
                )}
            </header>

            {editing !== null && (
                <SalesRepForm
                    key={editing === "new" ? "new" : editing.id}
                    rep={editing === "new" ? null : editing}
                    reps={all}
                    users={userOptions}
                    onDone={close}
                    onCancel={close}
                />
            )}
            {users.isError && (
                <p role="alert" className="text-sm text-late">
                    {users.error.message}
                </p>
            )}

            {reps.isLoading && <Skeleton className="h-40 w-full" />}
            {reps.isError && (
                <p role="alert" className="text-sm text-late">
                    {reps.error.message}
                </p>
            )}
            {reps.data && all.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    No sales reps yet.
                </p>
            )}

            {all.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {all.map((rep) => (
                        <RepRow
                            key={rep.id}
                            rep={rep}
                            manager={
                                rep.managerId
                                    ? byId.get(rep.managerId)
                                    : undefined
                            }
                            canAssign={canAssign}
                            onEdit={() => setEditing(rep)}
                        />
                    ))}
                </ul>
            )}
        </div>
    );
}

function RepRow({
    rep,
    manager,
    canAssign,
    onEdit,
}: {
    rep: SalesRep;
    manager: SalesRep | undefined;
    canAssign: boolean;
    onEdit: () => void;
}) {
    const save = useCustomerSave("", `/api/sales-reps/${rep.id}`);
    const active = rep.status === "Active";

    return (
        <li className="space-y-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {rep.name}
                        {!active && <StatusChip status={rep.status} />}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {[rep.region, manager && `Reports to ${manager.name}`]
                            .filter(Boolean)
                            .join(" · ") || "No region"}
                    </span>
                    <span
                        className={cn(
                            "block text-sm break-all",
                            rep.userEmail ? "" : "text-muted-foreground",
                        )}
                    >
                        {rep.userEmail ?? "No login linked"}
                    </span>
                </div>
                {canAssign && (
                    <div className="flex flex-wrap gap-2">
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={onEdit}
                        >
                            Edit
                        </Button>
                        <Button
                            variant="outline"
                            tone={active ? "danger" : undefined}
                            className="h-11"
                            disabled={save.isPending}
                            onClick={() =>
                                save.mutate({
                                    name: rep.name,
                                    region: rep.region,
                                    userId: rep.userId,
                                    managerId: rep.managerId,
                                    status: active ? "Inactive" : "Active",
                                })
                            }
                        >
                            {active ? "Deactivate" : "Reactivate"}
                        </Button>
                    </div>
                )}
            </div>
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
        </li>
    );
}
