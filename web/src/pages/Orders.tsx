import { useDeferredValue, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { LuPlus, LuSearch, LuTriangleAlert } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { Field } from "@/components/orders/Field";
import { StatusChip } from "@/components/orders/StatusChip";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import { cft } from "@/lib/format";
import {
    money,
    shortDate,
    stageLabel,
    statusLabel,
    type ApprovalStage,
    type OrderStatus,
    type OrderSummary,
    type Paged,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

const statuses = Object.keys(statusLabel) as OrderStatus[];

export function Orders() {
    const me = useMe().data!;
    const [search, setSearch] = useState("");
    const [status, setStatus] = useState<OrderStatus | "">("");
    const [stage, setStage] = useState<ApprovalStage | "">("");
    const [page, setPage] = useState(1);
    const deferred = useDeferredValue(search.trim());

    // The approval stages this person can decide, so "waiting for you" is one tap away.
    const mine = (
        [
            [PERMISSIONS.ordersApproveSalesManager, "SalesManager"],
            [PERMISSIONS.ordersApproveManagement, "Management"],
            [PERMISSIONS.ordersApproveCredit, "Credit"],
        ] as const
    ).filter(([permission]) => can(me, permission));

    const params = new URLSearchParams({ page: String(page), pageSize: "20" });
    if (deferred) params.set("search", deferred);
    if (status) params.set("status", status);
    if (stage) params.set("awaitingStage", stage);

    const orders = useQuery({
        queryKey: ["orders", params.toString()],
        queryFn: () => api<Paged<OrderSummary>>(`/api/orders?${params}`),
        placeholderData: (previous) => previous,
    });

    const data = orders.data;
    const first = data ? (data.page - 1) * data.pageSize + 1 : 0;
    const last = data ? first + data.items.length - 1 : 0;
    const pages = data
        ? Math.max(1, Math.ceil(data.totalCount / data.pageSize))
        : 1;

    const change = (apply: () => void) => {
        apply();
        setPage(1);
    };

    return (
        <div className="space-y-5">
            <header className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 className="display text-4xl leading-none">Orders</h1>
                    <p className="mt-2 text-sm text-muted-foreground">
                        From first quote to final invoice. Open an order to see
                        what it needs next.
                    </p>
                </div>
                {can(me, PERMISSIONS.ordersCreate) && (
                    <Link
                        to="/orders/new"
                        className={cn(
                            "inline-flex h-11 items-center gap-2 rounded-sm px-5 text-sm",
                            nextButtonClass,
                        )}
                    >
                        <LuPlus aria-hidden /> New order
                    </Link>
                )}
            </header>

            {mine.length > 0 && (
                <div
                    className="flex flex-wrap items-center gap-2"
                    role="group"
                    aria-label="Waiting for you"
                >
                    <span className="text-sm text-muted-foreground">
                        Waiting for you:
                    </span>
                    {mine.map(([, key]) => (
                        <button
                            key={key}
                            type="button"
                            aria-pressed={stage === key}
                            onClick={() =>
                                change(() => setStage(stage === key ? "" : key))
                            }
                            className={cn(
                                "h-9 rounded-sm border px-3 text-sm font-semibold",
                                stage === key
                                    ? "border-signal bg-signal text-ink"
                                    : "bg-card hover:border-ink",
                            )}
                        >
                            {stageLabel[key]} approval
                        </button>
                    ))}
                </div>
            )}

            <div className="grid gap-3 sm:grid-cols-[1fr_14rem]">
                <Field label="Search">
                    <span className="relative block">
                        <LuSearch
                            aria-hidden
                            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                        />
                        <input
                            value={search}
                            onChange={(e) =>
                                change(() => setSearch(e.target.value))
                            }
                            placeholder="Order number, customer or site"
                            className={cn(inputClass, "pl-9")}
                        />
                    </span>
                </Field>
                <Field label="Status">
                    <select
                        value={status}
                        onChange={(e) =>
                            change(() =>
                                setStatus(e.target.value as OrderStatus | ""),
                            )
                        }
                        className={inputClass}
                    >
                        <option value="">All orders</option>
                        {statuses.map((s) => (
                            <option key={s} value={s}>
                                {statusLabel[s]}
                            </option>
                        ))}
                    </select>
                </Field>
            </div>

            {orders.isPending ? (
                <Skeleton className="h-64 w-full" />
            ) : orders.isError ? (
                <p className="text-sm font-medium text-late" role="alert">
                    The orders could not be loaded. {orders.error.message}
                </p>
            ) : data!.items.length === 0 ? (
                <p
                    className={cn(
                        panelClass,
                        "px-4 py-12 text-center text-sm text-muted-foreground",
                    )}
                >
                    {deferred || status || stage
                        ? "No order matches. Clear the search or the filters to see more."
                        : can(me, PERMISSIONS.ordersCreate)
                          ? "There are no orders yet. Start with New order."
                          : "There are no orders yet."}
                </p>
            ) : (
                <section className={cn(panelClass, "overflow-hidden")}>
                    <div className="hidden grid-cols-[7rem_1fr_6rem_7rem_6rem_8rem_10rem] gap-3 border-b bg-muted/50 px-4 py-2 text-xs font-medium text-muted-foreground md:grid">
                        <span>Order</span>
                        <span>Customer and site</span>
                        <span>Plant</span>
                        <span>Needed on</span>
                        <span className="text-right">CFT</span>
                        <span className="text-right">Value</span>
                        <span>Status</span>
                    </div>
                    <ul className="divide-y">
                        {data!.items.map((o) => (
                            <li key={o.id}>
                                <Link
                                    to={`/orders/${o.id}`}
                                    className="grid gap-x-3 gap-y-1 px-4 py-3 hover:bg-muted/40 md:grid-cols-[7rem_1fr_6rem_7rem_6rem_8rem_10rem] md:items-center"
                                >
                                    <span className="font-semibold tabular-nums">
                                        {o.number}
                                    </span>
                                    <span className="min-w-0">
                                        <span className="block truncate font-medium">
                                            {o.customerName}
                                        </span>
                                        <span className="block truncate text-xs text-muted-foreground">
                                            {o.siteName}
                                        </span>
                                    </span>
                                    <span className="text-sm text-muted-foreground">
                                        {o.plantCode}
                                    </span>
                                    <span className="text-sm">
                                        {shortDate(o.requestedDate)}
                                    </span>
                                    <span className="text-sm tabular-nums md:text-right">
                                        {cft(o.totalQuantityCft)}
                                    </span>
                                    <span className="text-sm tabular-nums md:text-right">
                                        {money(o.totalValue)}
                                    </span>
                                    <span className="flex flex-wrap items-center gap-2">
                                        <StatusChip status={o.status} />
                                        {o.belowFloorLineCount > 0 && (
                                            <span
                                                title="A line is priced under the floor price"
                                                className="inline-flex items-center gap-1 text-xs font-medium text-late"
                                            >
                                                <LuTriangleAlert
                                                    aria-hidden
                                                    className="size-3.5"
                                                />
                                                Below floor
                                            </span>
                                        )}
                                    </span>
                                </Link>
                            </li>
                        ))}
                    </ul>
                    <footer className="flex items-center justify-between gap-3 border-t px-4 py-2 text-sm text-muted-foreground">
                        <span>
                            {first}–{last} of {data!.totalCount}
                        </span>
                        <span className="flex gap-2">
                            <button
                                type="button"
                                disabled={page <= 1}
                                onClick={() => setPage(page - 1)}
                                className="h-9 rounded-sm border bg-card px-3 text-foreground hover:border-ink disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                disabled={page >= pages}
                                onClick={() => setPage(page + 1)}
                                className="h-9 rounded-sm border bg-card px-3 text-foreground hover:border-ink disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Next
                            </button>
                        </span>
                    </footer>
                </section>
            )}
        </div>
    );
}
