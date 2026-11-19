import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "./Field";
import { parseAmount } from "./orderDraft";
import { inputClass, nextButtonClass, panelClass } from "./styles";
import { useOrderAct } from "./useOrderAct";
import { api } from "@/lib/api";
import { cft } from "@/lib/format";
import {
    adjustmentLabel,
    basisLabel,
    money,
    type AdjustmentKind,
    type BillingBasis,
    type Invoice,
    type InvoiceLine,
    type OrderStatus,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

export function BillingPanel({
    orderId,
    status,
}: {
    orderId: string;
    status: OrderStatus;
}) {
    const me = useMe().data!;
    const act = useOrderAct();
    const invoice = useQuery({
        queryKey: ["invoice", orderId],
        queryFn: () => api<Invoice>(`/api/orders/${orderId}/invoice`),
    });

    const running = status === "InProgress";
    const open = status === "Confirmed" || status === "InProgress";
    const canClose = can(me, PERMISSIONS.ordersClose);
    const canResolve = can(me, PERMISSIONS.ordersBillingResolve);

    if (invoice.isPending) return null;
    if (invoice.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                The invoice could not be loaded. {invoice.error.message}
            </p>
        );
    }
    const inv = invoice.data;
    const url = `/api/orders/${orderId}`;
    const unresolved = inv.lines.filter((l) => l.needsResolution);

    return (
        <section className={panelClass} aria-label="Delivery and billing">
            <header className="border-b px-4 py-3">
                <h2 className="display text-2xl">
                    {inv.final ? "Invoice" : "Delivery and billing"}
                </h2>
                <p className="text-xs text-muted-foreground">
                    {inv.final
                        ? "The order is closed. These figures are final."
                        : "These figures are provisional until the order is closed."}
                </p>
            </header>

            <div className="overflow-x-auto">
                <table className="w-full min-w-152 text-sm">
                    <thead className="border-b bg-muted/50 text-left text-xs font-medium text-muted-foreground">
                        <tr>
                            <th className="px-4 py-2">Mix</th>
                            <th className="px-4 py-2 text-right">Ordered</th>
                            <th className="px-4 py-2 text-right">Delivered</th>
                            <th className="px-4 py-2 text-right">Billed</th>
                            <th className="px-4 py-2 text-right">Amount</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y">
                        {inv.lines.map((l) => (
                            <tr key={l.lineId}>
                                <td className="px-4 py-2 font-medium">
                                    {l.productCode}
                                    {l.needsResolution && (
                                        <span className="ml-2 rounded-sm border border-late/40 bg-late/10 px-1.5 py-0.5 text-xs font-semibold text-late">
                                            Needs a decision
                                        </span>
                                    )}
                                    {l.basis && (
                                        <span className="block text-xs font-normal text-muted-foreground">
                                            {basisLabel[l.basis]}
                                            {l.note ? `: ${l.note}` : ""}
                                        </span>
                                    )}
                                </td>
                                <td className="px-4 py-2 text-right tabular-nums">
                                    {cft(l.orderedCft)}
                                </td>
                                <td className="px-4 py-2 text-right tabular-nums">
                                    {cft(l.deliveredCft)}
                                </td>
                                <td className="px-4 py-2 text-right tabular-nums">
                                    {l.billedCft !== null
                                        ? cft(l.billedCft)
                                        : "Not decided"}
                                </td>
                                <td className="px-4 py-2 text-right tabular-nums">
                                    {money(l.amount)}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {open && canClose && (
                <DeliveriesForm
                    key={inv.lines.map((l) => l.deliveredCft).join("|")}
                    lines={inv.lines}
                    pending={act.isPending}
                    onSave={(lines) =>
                        act.mutate({
                            url: `${url}/deliveries`,
                            method: "PUT",
                            body: { lines },
                        })
                    }
                />
            )}

            {running && canResolve && unresolved.length > 0 && (
                <div className="space-y-3 border-t px-4 py-4">
                    <h3 className="font-semibold">
                        Delivered and ordered differ
                    </h3>
                    <p className="text-sm text-muted-foreground">
                        Decide what the customer is billed for. Closing the
                        order needs every line decided.
                    </p>
                    {unresolved.map((l) => (
                        <ResolveRow
                            key={l.lineId}
                            line={l}
                            pending={act.isPending}
                            onSave={(line) =>
                                act.mutate({
                                    url: `${url}/billing`,
                                    method: "PUT",
                                    body: { lines: [line] },
                                })
                            }
                        />
                    ))}
                </div>
            )}

            {(inv.adjustments.length > 0 || (running && canResolve)) && (
                <div className="space-y-3 border-t px-4 py-4">
                    <h3 className="font-semibold">Adjustments</h3>
                    {inv.adjustments.length === 0 && (
                        <p className="text-sm text-muted-foreground">
                            Nothing has been taken off. Add a return, a QC
                            reject or a discount if the customer should pay
                            less.
                        </p>
                    )}
                    <ul className="divide-y rounded-sm border">
                        {inv.adjustments.map((a) => (
                            <li
                                key={a.id}
                                className="flex flex-wrap items-center justify-between gap-3 px-3 py-2 text-sm"
                            >
                                <span>
                                    <span className="font-medium">
                                        {adjustmentLabel[a.kind]}
                                    </span>
                                    , {a.reason}
                                    {a.quantityCft !== null && (
                                        <span className="text-muted-foreground">
                                            {" "}
                                            ({cft(a.quantityCft)} CFT)
                                        </span>
                                    )}
                                </span>
                                <span className="flex items-center gap-3">
                                    <span className="tabular-nums">
                                        - {money(a.amount)}
                                    </span>
                                    {running && canResolve && (
                                        <button
                                            type="button"
                                            disabled={act.isPending}
                                            onClick={() =>
                                                act.mutate({
                                                    url: `${url}/adjustments/${a.id}`,
                                                    method: "DELETE",
                                                })
                                            }
                                            className="text-xs text-late underline-offset-2 hover:underline"
                                        >
                                            Remove
                                        </button>
                                    )}
                                </span>
                            </li>
                        ))}
                    </ul>
                    {running && canResolve && (
                        <AdjustmentForm
                            pending={act.isPending}
                            onAdd={(body) =>
                                act.mutate({ url: `${url}/adjustments`, body })
                            }
                        />
                    )}
                </div>
            )}

            <dl className="space-y-1 border-t px-4 py-4 text-sm">
                <Row label="Concrete" value={money(inv.concreteAmount)} />
                {inv.chargesAmount !== 0 && (
                    <Row label="Charges" value={money(inv.chargesAmount)} />
                )}
                {inv.adjustmentsAmount !== 0 && (
                    <Row
                        label="Adjustments"
                        value={`- ${money(Math.abs(inv.adjustmentsAmount))}`}
                    />
                )}
                <Row label="Subtotal" value={money(inv.subtotal)} />
                <Row
                    label={
                        inv.vatExempt
                            ? "VAT, customer is exempt"
                            : `VAT ${inv.vatPercent}%`
                    }
                    value={money(inv.vatAmount)}
                />
                <Row label="Total" value={money(inv.total)} strong />
            </dl>

            {running && canClose && (
                <footer className="space-y-2 border-t px-4 py-4">
                    <Button
                        className={cn(
                            "h-11 px-6",
                            inv.canClose && nextButtonClass,
                        )}
                        disabled={!inv.canClose || act.isPending}
                        onClick={() => act.mutate({ url: `${url}/close` })}
                    >
                        Close the order
                    </Button>
                    {!inv.canClose && (
                        <p className="text-sm text-muted-foreground">
                            {unresolved.length > 0
                                ? "Decide what is billed on each line marked above, then close."
                                : "The order cannot be closed yet. Record what was delivered first."}
                        </p>
                    )}
                </footer>
            )}

            {act.isError && (
                <p
                    className="border-t px-4 py-3 text-sm font-medium text-late"
                    role="alert"
                >
                    {act.error.message}
                </p>
            )}
        </section>
    );
}

function Row({
    label,
    value,
    strong,
}: {
    label: string;
    value: string;
    strong?: boolean;
}) {
    return (
        <div
            className={cn(
                "flex justify-between gap-4",
                strong && "border-t pt-2 text-base font-semibold",
            )}
        >
            <dt className={strong ? "" : "text-muted-foreground"}>{label}</dt>
            <dd className="tabular-nums">{value}</dd>
        </div>
    );
}

function DeliveriesForm({
    lines,
    pending,
    onSave,
}: {
    lines: InvoiceLine[];
    pending: boolean;
    onSave: (lines: { lineId: string; deliveredCft: number }[]) => void;
}) {
    const [values, setValues] = useState<Record<string, string>>(() =>
        Object.fromEntries(
            lines.map((l) => [l.lineId, String(l.deliveredCft)]),
        ),
    );
    const parsed = lines.map((l) => parseAmount(values[l.lineId] ?? ""));
    const ok = parsed.every(
        (v) => v !== null && v >= 0 && Math.round(v * 100) === v * 100,
    );

    return (
        <div className="space-y-3 border-t px-4 py-4">
            <h3 className="font-semibold">Record what was delivered</h3>
            <div className="grid gap-3 sm:grid-cols-2">
                {lines.map((l) => (
                    <Field
                        key={l.lineId}
                        label={`${l.productCode}, CFT delivered`}
                    >
                        <input
                            inputMode="decimal"
                            value={values[l.lineId] ?? ""}
                            onChange={(e) =>
                                setValues({
                                    ...values,
                                    [l.lineId]: e.target.value,
                                })
                            }
                            className={inputClass}
                        />
                    </Field>
                ))}
            </div>
            <Button
                variant="outline"
                className="h-10 px-4"
                disabled={!ok || pending}
                onClick={() =>
                    onSave(
                        lines.map((l, i) => ({
                            lineId: l.lineId,
                            deliveredCft: parsed[i]!,
                        })),
                    )
                }
            >
                Save deliveries
            </Button>
        </div>
    );
}

function ResolveRow({
    line,
    pending,
    onSave,
}: {
    line: InvoiceLine;
    pending: boolean;
    onSave: (line: {
        lineId: string;
        basis: BillingBasis;
        billedCft?: number;
        reason: string;
    }) => void;
}) {
    const [basis, setBasis] = useState<BillingBasis>("Delivered");
    const [custom, setCustom] = useState("");
    const [reason, setReason] = useState("");
    const customValue = parseAmount(custom);
    const ok =
        reason.trim().length >= 3 &&
        (basis !== "Custom" ||
            (customValue !== null &&
                customValue >= 0 &&
                Math.round(customValue * 100) === customValue * 100));

    return (
        <div className="rounded-sm border bg-muted/30 p-3">
            <p className="text-sm font-medium">
                {line.productCode}: ordered {cft(line.orderedCft)}, delivered{" "}
                {cft(line.deliveredCft)}
            </p>
            <div
                className="mt-2 flex flex-wrap gap-2"
                role="radiogroup"
                aria-label="Bill for"
            >
                {(Object.keys(basisLabel) as BillingBasis[]).map((b) => (
                    <button
                        key={b}
                        type="button"
                        role="radio"
                        aria-checked={basis === b}
                        onClick={() => setBasis(b)}
                        className={cn(
                            "h-9 rounded-sm border px-3 text-sm",
                            basis === b
                                ? "border-ink bg-ink text-white"
                                : "bg-card hover:border-ink",
                        )}
                    >
                        {basisLabel[b]}
                    </button>
                ))}
            </div>
            <div className="mt-3 grid gap-3 sm:grid-cols-[10rem_1fr]">
                {basis === "Custom" && (
                    <Field label="CFT to bill">
                        <input
                            inputMode="decimal"
                            value={custom}
                            onChange={(e) => setCustom(e.target.value)}
                            className={inputClass}
                        />
                    </Field>
                )}
                <Field
                    label="Why?"
                    className={basis === "Custom" ? "" : "sm:col-span-2"}
                >
                    <input
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        maxLength={500}
                        className={inputClass}
                    />
                </Field>
            </div>
            <Button
                className="mt-3 h-10 px-4"
                disabled={!ok || pending}
                onClick={() =>
                    onSave({
                        lineId: line.lineId,
                        basis,
                        billedCft:
                            basis === "Custom" ? customValue! : undefined,
                        reason: reason.trim(),
                    })
                }
            >
                Save decision
            </Button>
        </div>
    );
}

function AdjustmentForm({
    pending,
    onAdd,
}: {
    pending: boolean;
    onAdd: (body: {
        kind: AdjustmentKind;
        reason: string;
        quantityCft?: number;
        amount: number;
    }) => void;
}) {
    const [kind, setKind] = useState<AdjustmentKind>("Return");
    const [reason, setReason] = useState("");
    const [quantity, setQuantity] = useState("");
    const [amount, setAmount] = useState("");
    const amountValue = parseAmount(amount);
    const quantityValue = parseAmount(quantity);
    const ok =
        reason.trim().length >= 3 &&
        amountValue !== null &&
        amountValue > 0 &&
        (quantity.trim() === "" ||
            (quantityValue !== null && quantityValue > 0));

    return (
        <div className="grid gap-3 rounded-sm border bg-muted/30 p-3 sm:grid-cols-4">
            <Field label="Kind">
                <select
                    value={kind}
                    onChange={(e) => setKind(e.target.value as AdjustmentKind)}
                    className={inputClass}
                >
                    {(Object.keys(adjustmentLabel) as AdjustmentKind[]).map(
                        (k) => (
                            <option key={k} value={k}>
                                {adjustmentLabel[k]}
                            </option>
                        ),
                    )}
                </select>
            </Field>
            <Field label="Why?" className="sm:col-span-2">
                <input
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="CFT (optional)">
                <input
                    inputMode="decimal"
                    value={quantity}
                    onChange={(e) => setQuantity(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Amount taken off, before VAT"
                className="sm:col-span-2"
            >
                <input
                    inputMode="decimal"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <div className="flex items-end">
                <Button
                    variant="outline"
                    className="h-10 px-4"
                    disabled={!ok || pending}
                    onClick={() => {
                        onAdd({
                            kind,
                            reason: reason.trim(),
                            quantityCft:
                                quantity.trim() === ""
                                    ? undefined
                                    : quantityValue!,
                            amount: amountValue!,
                        });
                        setReason("");
                        setQuantity("");
                        setAmount("");
                    }}
                >
                    Add adjustment
                </Button>
            </div>
        </div>
    );
}
