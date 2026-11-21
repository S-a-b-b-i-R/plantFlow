import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { LuChevronRight, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { CustomerForm } from "@/components/customers/CustomerForms";
import {
    useCustomerList,
    useMarkOpenedSeen,
    useMyRep,
    useOpenedCount,
} from "@/components/customers/useCustomers";
import { OpButton as Button } from "@/components/operator/OpButton";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { StatusChip } from "@/components/plants/StatusChip";
import { OpenChip } from "@/components/customers/OpenChip";
import type { StatusFilter } from "@/components/plants/usePlants";
import { Skeleton } from "@/components/ui/skeleton";
import type { OwnershipFilter } from "@/lib/customers";
import { cn } from "@/lib/utils";

// The company's customers. Opening one shows its sites. A sales rep sees their own customers and
// the open ones (plus any where an order of theirs is still under way).
export function Customers() {
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.customersManage);
    const canAssign = can(me, PERMISSIONS.customersAssign);
    const myRep = useMyRep();
    const navigate = useNavigate();
    const [search, setSearch] = useState("");
    const [status, setStatus] = useState<StatusFilter>("Active");
    const [ownership, setOwnership] = useState<OwnershipFilter>("All");
    const [adding, setAdding] = useState(false);
    const customers = useCustomerList(search, status, ownership);
    const isRep = canManage && !canAssign;
    const opened = useOpenedCount(isRep);
    const markSeen = useMarkOpenedSeen();
    const newlyOpen = opened.data?.count ?? 0;

    const showOwnership = (value: OwnershipFilter) => {
        setOwnership(value);
        if (value === "Open" && isRep) markSeen.mutate();
    };
    const items = customers.data?.items ?? [];
    const total = customers.data?.totalCount ?? 0;

    // A sales rep adds customers as their own, so they need a linked rep record first.
    const needsRepLink =
        canManage && !canAssign && myRep.isSuccess && !myRep.data;
    const canAdd =
        canManage && (canAssign || (myRep.isSuccess && !!myRep.data));

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">Customers</h1>
                    <p className="mt-2 text-sm text-muted-foreground">
                        Who buys concrete, and the sites they pour at.
                    </p>
                </div>
                {canAdd && !adding && (
                    <Button
                        className={cn("h-11 px-4", nextButtonClass)}
                        onClick={() => setAdding(true)}
                    >
                        <LuPlus aria-hidden /> New customer
                    </Button>
                )}
            </header>

            {needsRepLink && (
                <p className="rounded-sm border border-ink/30 bg-card p-3 text-sm">
                    Your login is not linked to a sales rep yet, so you cannot
                    add customers. Ask a sales manager to link it on the Sales
                    reps screen.
                </p>
            )}

            {adding && (
                <CustomerForm
                    customer={null}
                    canAssign={canAssign}
                    onDone={(saved) => {
                        setAdding(false);
                        if (saved) navigate(`/customers/${saved.id}`);
                    }}
                    onCancel={() => setAdding(false)}
                />
            )}

            {newlyOpen > 0 && ownership !== "Open" && (
                <button
                    type="button"
                    onClick={() => showOwnership("Open")}
                    className="flex min-h-11 w-full items-center justify-between gap-3 rounded-sm border border-ink/30 bg-card px-3 py-2 text-left text-sm hover:bg-muted/50"
                >
                    <span>
                        {newlyOpen === 1
                            ? "1 customer opened up since you last looked."
                            : `${newlyOpen} customers opened up since you last looked.`}
                    </span>
                    <span className="font-semibold underline">Show open</span>
                </button>
            )}

            <div className="flex flex-wrap gap-2">
                <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search code, name or phone"
                    aria-label="Search customers"
                    className={cn(inputClass, "h-11 min-w-0 flex-1")}
                />
                <select
                    value={status}
                    onChange={(e) => setStatus(e.target.value as StatusFilter)}
                    aria-label="Show"
                    className={cn(inputClass, "h-11 w-auto")}
                >
                    <option value="Active">Active</option>
                    <option value="Inactive">Inactive</option>
                    <option value="All">All</option>
                </select>
                <select
                    value={ownership}
                    onChange={(e) =>
                        showOwnership(e.target.value as OwnershipFilter)
                    }
                    aria-label="Owner"
                    className={cn(inputClass, "h-11 w-auto")}
                >
                    <option value="All">
                        {isRep ? "Mine and open" : "Any owner"}
                    </option>
                    {isRep && <option value="Mine">Mine</option>}
                    <option value="Open">Open</option>
                </select>
            </div>

            {customers.isLoading && <Skeleton className="h-40 w-full" />}
            {customers.isError && (
                <p role="alert" className="text-sm text-late">
                    {customers.error.message}
                </p>
            )}
            {customers.data && items.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    {ownership === "Open" && !search.trim()
                        ? "No open customers right now."
                        : search.trim() ||
                            status !== "Active" ||
                            ownership !== "All"
                          ? "No customers match."
                          : "No customers yet."}
                </p>
            )}

            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((customer) => (
                        <li key={customer.id}>
                            <Link
                                to={`/customers/${customer.id}`}
                                className="flex min-h-14 items-center gap-3 px-4 py-3 hover:bg-muted/50"
                            >
                                <div className="min-w-0 flex-1">
                                    <span className="flex flex-wrap items-center gap-2">
                                        <span className="font-semibold">
                                            {customer.code}
                                        </span>
                                        <span className="truncate">
                                            {customer.name}
                                        </span>
                                        {customer.status === "Inactive" && (
                                            <StatusChip
                                                status={customer.status}
                                            />
                                        )}
                                        {customer.isOpen && <OpenChip />}
                                    </span>
                                    <span className="mt-0.5 block text-xs text-muted-foreground">
                                        {customer.type} ·{" "}
                                        {customer.siteCount === 1
                                            ? "1 site"
                                            : `${customer.siteCount} sites`}
                                        {customer.salesRepName &&
                                            !customer.isOpen &&
                                            ` · ${customer.salesRepName}`}
                                    </span>
                                </div>
                                <LuChevronRight
                                    aria-hidden
                                    className="size-5 shrink-0 text-muted-foreground"
                                />
                            </Link>
                        </li>
                    ))}
                </ul>
            )}
            {total > items.length && (
                <p className="text-xs text-muted-foreground">
                    Showing {items.length} of {total}. Search to narrow the
                    list.
                </p>
            )}
        </div>
    );
}
