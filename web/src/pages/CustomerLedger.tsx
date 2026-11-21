import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { LuChevronLeft, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { useProducts } from "@/components/catalogue/useCatalogue";
import { CreditCard } from "@/components/ledger/CreditCard";
import {
    useLedgerAct,
    useLedgerStatement,
} from "@/components/ledger/useLedger";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { parseAmount } from "@/components/orders/orderDraft";
import { inputClass, panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import { money, shortDate, statusLabel } from "@/lib/orders";
import { cn } from "@/lib/utils";

// One customer's ledger: each credit with its history, then every order with what was ordered,
// delivered and billed.
export function CustomerLedger() {
    const { customerId = "" } = useParams();
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.ledgerManage);
    const [page, setPage] = useState(1);
    const [adding, setAdding] = useState(false);
    const statement = useLedgerStatement(customerId, page);

    if (statement.isLoading) return <Skeleton className="h-60 w-full" />;
    if (statement.isError) {
        return (
            <p role="alert" className="text-sm text-late">
                The ledger could not be loaded. {statement.error.message}
            </p>
        );
    }

    const s = statement.data!;
    const open = s.credits.filter(
        (c) => c.balanceCft > 0 || c.pendingCloseCft > 0,
    );
    const spent = s.credits.filter(
        (c) => c.balanceCft === 0 && c.pendingCloseCft === 0,
    );
    const pages = Math.max(
        1,
        Math.ceil(s.orders.totalCount / s.orders.pageSize),
    );

    return (
        <div className="space-y-6">
            <div className="space-y-3">
                <Link
                    to="/ledger"
                    className="inline-flex min-h-11 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
                >
                    <LuChevronLeft aria-hidden className="size-4" />
                    Customer ledger
                </Link>
                <header className="flex flex-wrap items-end justify-between gap-3">
                    <div className="min-w-0">
                        <p className="text-sm font-semibold text-muted-foreground">
                            {s.code}
                        </p>
                        <h1 className="display text-4xl leading-none">
                            {s.name}
                        </h1>
                    </div>
                    <div className="sm:text-right">
                        <p className="display text-3xl leading-none tabular-nums">
                            {cft(s.openCft)} CFT
                        </p>
                        <p className="text-xs text-muted-foreground tabular-nums">
                            credit to use, worth {money(s.openValue)}
                        </p>
                    </div>
                </header>
            </div>

            <section className="space-y-3" aria-label="Credits">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <h2 className="display text-2xl">Credits</h2>
                    {canManage && !adding && (
                        <Button
                            variant="outline"
                            className="h-11 px-4"
                            onClick={() => setAdding(true)}
                        >
                            <LuPlus aria-hidden className="size-4" />
                            Add a credit
                        </Button>
                    )}
                </div>
                {adding && (
                    <AddCreditForm
                        customerId={customerId}
                        onDone={() => setAdding(false)}
                    />
                )}
                {s.credits.length === 0 && !adding && (
                    <p
                        className={cn(
                            panelClass,
                            "p-4 text-sm text-muted-foreground",
                        )}
                    >
                        This customer has no credit. Credit is added when an
                        order closes short and the rest is carried forward
                        {canManage
                            ? ", or by hand for a balance from your old records."
                            : "."}
                    </p>
                )}
                {open.length > 0 && (
                    <div className="grid items-start gap-3 lg:grid-cols-2">
                        {open.map((c) => (
                            <CreditCard
                                key={c.id}
                                credit={c}
                                canManage={canManage}
                            />
                        ))}
                    </div>
                )}
                {spent.length > 0 && (
                    <details className="group">
                        <summary className="flex min-h-11 cursor-pointer items-center text-sm font-medium text-muted-foreground">
                            {spent.length} used up or closed
                        </summary>
                        <div className="mt-2 grid items-start gap-3 lg:grid-cols-2">
                            {spent.map((c) => (
                                <CreditCard
                                    key={c.id}
                                    credit={c}
                                    canManage={canManage}
                                />
                            ))}
                        </div>
                    </details>
                )}
            </section>

            <section className="space-y-3" aria-label="Orders">
                <h2 className="display text-2xl">Orders</h2>
                {s.orders.items.length === 0 ? (
                    <p
                        className={cn(
                            panelClass,
                            "p-4 text-sm text-muted-foreground",
                        )}
                    >
                        No confirmed order yet.
                    </p>
                ) : (
                    <div className={cn(panelClass, "overflow-x-auto")}>
                        <table className="w-full min-w-152 text-sm">
                            <thead className="border-b bg-muted/50 text-left text-xs font-medium text-muted-foreground">
                                <tr>
                                    <th className="px-4 py-2">Order</th>
                                    <th className="px-4 py-2 text-right">
                                        Ordered
                                    </th>
                                    <th className="px-4 py-2 text-right">
                                        Delivered
                                    </th>
                                    <th className="px-4 py-2 text-right">
                                        Billed
                                    </th>
                                    <th className="px-4 py-2 text-right">
                                        Credit
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y">
                                {s.orders.items.map((o) => (
                                    <tr key={o.orderId}>
                                        <td className="px-4 py-2">
                                            <Link
                                                to={`/orders/${o.orderId}`}
                                                className="font-medium underline-offset-2 hover:underline"
                                            >
                                                {o.number}
                                            </Link>
                                            <span className="block text-xs text-muted-foreground">
                                                {shortDate(o.requestedDate)} ·{" "}
                                                {statusLabel[o.status]}
                                            </span>
                                        </td>
                                        <td className="px-4 py-2 text-right tabular-nums">
                                            {cft(o.orderedCft)}
                                        </td>
                                        <td className="px-4 py-2 text-right tabular-nums">
                                            {cft(o.deliveredCft)}
                                        </td>
                                        <td className="px-4 py-2 text-right tabular-nums">
                                            {cft(o.billedCft)}
                                            {!o.final && (
                                                <span className="block text-xs text-muted-foreground">
                                                    provisional
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-4 py-2 text-right text-xs tabular-nums">
                                            <CreditCell
                                                used={o.creditUsedCft}
                                                carried={o.carriedForwardCft}
                                            />
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
                {pages > 1 && (
                    <div className="flex items-center justify-between gap-3 text-sm">
                        <Button
                            variant="outline"
                            className="h-11 px-4"
                            disabled={page <= 1}
                            onClick={() => setPage(page - 1)}
                        >
                            Newer
                        </Button>
                        <span className="text-muted-foreground">
                            Page {page} of {pages}
                        </span>
                        <Button
                            variant="outline"
                            className="h-11 px-4"
                            disabled={page >= pages}
                            onClick={() => setPage(page + 1)}
                        >
                            Older
                        </Button>
                    </div>
                )}
            </section>
        </div>
    );
}

function CreditCell({ used, carried }: { used: number; carried: number }) {
    const parts = [
        carried > 0 ? `+${cft(carried)} carried forward` : null,
        used > 0 ? `−${cft(used)} used` : null,
    ].filter((p) => p !== null);
    if (parts.length === 0) {
        return <span className="text-muted-foreground">–</span>;
    }
    return (
        <>
            {parts.map((p) => (
                <span key={p} className="block">
                    {p}
                </span>
            ))}
        </>
    );
}

// A credit Accounts enters by hand, for example a balance from the company's old records.
function AddCreditForm({
    customerId,
    onDone,
}: {
    customerId: string;
    onDone: () => void;
}) {
    const products = useProducts();
    const act = useLedgerAct();
    const [productId, setProductId] = useState("");
    const [quantity, setQuantity] = useState("");
    const [price, setPrice] = useState("");
    const [reason, setReason] = useState("");
    const qty = parseAmount(quantity);
    const unit = parseAmount(price);
    const twoDecimals = (v: number) => Math.round(v * 100) === v * 100;
    const ok =
        productId !== "" &&
        qty !== null &&
        qty > 0 &&
        twoDecimals(qty) &&
        unit !== null &&
        unit >= 0 &&
        twoDecimals(unit) &&
        reason.trim().length >= 3;

    return (
        <div className={cn(panelClass, "space-y-3 p-4")}>
            <h3 className="font-semibold">Add a credit</h3>
            <div className="grid gap-3 sm:grid-cols-3">
                <Field label="Mix" className="sm:col-span-3">
                    <select
                        value={productId}
                        onChange={(e) => setProductId(e.target.value)}
                        className={cn(inputClass, "h-11")}
                    >
                        <option value="">Choose a mix</option>
                        {products.data?.items.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.code}, {p.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="CFT">
                    <input
                        inputMode="decimal"
                        value={quantity}
                        onChange={(e) => setQuantity(e.target.value)}
                        className={cn(inputClass, "h-11")}
                    />
                </Field>
                <Field label="Price per CFT, before VAT">
                    <input
                        inputMode="decimal"
                        value={price}
                        onChange={(e) => setPrice(e.target.value)}
                        className={cn(inputClass, "h-11")}
                    />
                </Field>
                <Field
                    label="Why?"
                    hint="For example: balance from the old ledger."
                >
                    <input
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        maxLength={500}
                        className={cn(inputClass, "h-11")}
                    />
                </Field>
            </div>
            <div className="flex flex-wrap gap-2">
                <Button
                    className="h-11 px-4"
                    disabled={!ok || act.isPending}
                    onClick={() =>
                        act.mutate(
                            {
                                url: `/api/customers/${customerId}/ledger/credits`,
                                body: {
                                    productId,
                                    quantityCft: qty,
                                    unitPrice: unit,
                                    reason: reason.trim(),
                                },
                            },
                            { onSuccess: onDone },
                        )
                    }
                >
                    Add credit
                </Button>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={onDone}
                >
                    Cancel
                </Button>
            </div>
            {act.isError && (
                <p role="alert" className="text-sm text-late">
                    {act.error.message}
                </p>
            )}
        </div>
    );
}
