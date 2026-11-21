import { useDeferredValue, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { LuChevronRight } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { CloseQueue } from "@/components/ledger/CloseQueue";
import {
    useLedgerCustomers,
    usePendingCloses,
} from "@/components/ledger/useLedger";
import {
    inputClass,
    panelClass,
    tabClass,
    tabRowClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import { money } from "@/lib/orders";
import { cn } from "@/lib/utils";

// The customer volume ledger: customers with credit to use, and credit closes waiting for approval.
// Each person sees the tabs their permissions allow. An approver who can't see balances lands on To approve.
export function Ledger() {
    const me = useMe().data!;
    const canView = can(me, PERMISSIONS.ledgerView);
    const canApprove = can(me, PERMISSIONS.ledgerApprove);
    const closes = usePendingCloses(canApprove);
    const [params, setParams] = useSearchParams();
    const tab =
        canApprove && (params.get("tab") === "approve" || !canView)
            ? "approve"
            : "balances";
    const waiting = closes.data?.filter((c) => !c.requestedByMe).length ?? 0;

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">
                    Customer ledger
                </h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    Concrete a customer ordered but didn't take, kept at the
                    price they ordered it at for their next order of the same
                    mix.
                </p>
            </header>

            {canApprove && canView && (
                <nav className={tabRowClass} aria-label="Ledger lists">
                    <button
                        type="button"
                        className={tabClass(tab === "balances")}
                        aria-current={tab === "balances" ? "page" : undefined}
                        onClick={() => setParams({})}
                    >
                        Balances
                    </button>
                    <button
                        type="button"
                        className={tabClass(tab === "approve")}
                        aria-current={tab === "approve" ? "page" : undefined}
                        onClick={() => setParams({ tab: "approve" })}
                    >
                        To approve
                        {waiting > 0 && (
                            <span className="grid h-5 min-w-5 place-items-center rounded-full bg-ink px-1.5 text-xs font-bold text-white">
                                {waiting}
                            </span>
                        )}
                    </button>
                </nav>
            )}

            {tab === "approve" ? <CloseQueue /> : <Balances />}
        </div>
    );
}

function Balances() {
    const [search, setSearch] = useState("");
    const [all, setAll] = useState(false);
    const deferred = useDeferredValue(search.trim());
    const customers = useLedgerCustomers(deferred, all);

    return (
        <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Find a customer by name or code"
                    aria-label="Find a customer"
                    className={cn(
                        inputClass,
                        "h-11 w-full min-w-0 sm:w-auto sm:max-w-md sm:flex-1",
                    )}
                />
                <label className="flex min-h-11 items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        checked={all}
                        onChange={(e) => setAll(e.target.checked)}
                        className="size-5"
                    />
                    Include used-up credits
                </label>
            </div>

            {customers.isLoading && <Skeleton className="h-32 w-full" />}
            {customers.isError && (
                <p role="alert" className="text-sm text-late">
                    {customers.error.message}
                </p>
            )}
            {customers.data && customers.data.items.length === 0 && (
                <p
                    className={cn(
                        panelClass,
                        "p-4 text-sm text-muted-foreground",
                    )}
                >
                    {deferred
                        ? "No customer with credit matches that search."
                        : "No customer has credit to use. Credit appears here when an order closes short and the rest is carried forward."}
                </p>
            )}

            <ul className={cn(panelClass, "divide-y empty:hidden")}>
                {customers.data?.items.map((c) => (
                    <li key={c.customerId}>
                        <Link
                            to={`/ledger/${c.customerId}`}
                            className="flex min-h-16 items-center justify-between gap-3 px-4 py-3 hover:bg-muted/50"
                        >
                            <span className="min-w-0">
                                <span className="block font-semibold">
                                    {c.name}
                                </span>
                                <span className="block text-xs text-muted-foreground">
                                    {c.code} · {c.credits}{" "}
                                    {c.credits === 1 ? "credit" : "credits"}
                                    {c.pendingCloseCft > 0 &&
                                        ` · ${cft(c.pendingCloseCft)} CFT waiting to close`}
                                </span>
                            </span>
                            <span className="flex items-center gap-3">
                                <span className="text-right">
                                    <span className="block font-semibold tabular-nums">
                                        {cft(c.openCft)} CFT
                                    </span>
                                    <span className="block text-xs text-muted-foreground tabular-nums">
                                        {money(c.openValue)}
                                    </span>
                                </span>
                                <LuChevronRight
                                    aria-hidden
                                    className="size-5 shrink-0 text-muted-foreground"
                                />
                            </span>
                        </Link>
                    </li>
                ))}
            </ul>
        </div>
    );
}
