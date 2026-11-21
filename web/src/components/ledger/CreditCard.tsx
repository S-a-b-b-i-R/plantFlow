import { useState } from "react";
import { Link } from "react-router-dom";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { parseAmount } from "@/components/orders/orderDraft";
import { inputClass, panelClass } from "@/components/orders/styles";
import { cft } from "@/lib/format";
import {
    closeReasonLabel,
    closeStatusLabel,
    creditOrigin,
    entryLabel,
    type LedgerCloseReason,
    type LedgerCredit,
} from "@/lib/ledger";
import { dateTime, money } from "@/lib/orders";
import { cn } from "@/lib/utils";
import { useLedgerAct } from "./useLedger";

const twoDecimals = (v: number) => Math.round(v * 100) === v * 100;

// One credit: what is left, where it came from, its full history, and (for Accounts) correct or close it.
export function CreditCard({
    credit,
    canManage,
}: {
    credit: LedgerCredit;
    canManage: boolean;
}) {
    const [open, setOpen] = useState<"correct" | "close" | null>(null);
    const act = useLedgerAct();
    const done = () => setOpen(null);

    return (
        <article className={panelClass}>
            <header className="flex items-start justify-between gap-3 border-b px-4 py-3">
                <div className="min-w-0">
                    <h3 className="font-semibold">
                        {credit.productCode}{" "}
                        <span className="font-normal text-muted-foreground">
                            {credit.productName}
                        </span>
                    </h3>
                    <p className="text-xs text-muted-foreground">
                        {credit.sourceOrderId ? (
                            <Link
                                to={`/orders/${credit.sourceOrderId}`}
                                className="underline-offset-2 hover:underline"
                            >
                                {creditOrigin(credit)}
                            </Link>
                        ) : (
                            creditOrigin(credit)
                        )}{" "}
                        · {money(credit.unitPrice)} per CFT
                    </p>
                </div>
                <div className="shrink-0 text-right">
                    <p className="display text-2xl leading-none tabular-nums">
                        {cft(credit.balanceCft)} CFT
                    </p>
                    <p className="text-xs text-muted-foreground">
                        left of {cft(credit.originalCft)}
                    </p>
                    {credit.pendingCloseCft > 0 && (
                        <p className="mt-1 rounded-sm border border-signal bg-signal/20 px-1.5 py-0.5 text-xs font-semibold">
                            {cft(credit.pendingCloseCft)} CFT waiting to close
                        </p>
                    )}
                </div>
            </header>

            <ol className="divide-y text-sm">
                {credit.entries.map((e, i) => (
                    <li
                        key={i}
                        className="grid grid-cols-[1fr_auto] gap-x-3 px-4 py-2"
                    >
                        <span className="min-w-0">
                            <span className="font-medium">
                                {entryLabel[e.kind]}
                            </span>
                            {e.closeStatus && (
                                <span
                                    className={cn(
                                        "ml-2 text-xs",
                                        e.closeStatus === "SentBack"
                                            ? "text-late"
                                            : "text-muted-foreground",
                                    )}
                                >
                                    {closeStatusLabel[e.closeStatus]}
                                </span>
                            )}
                            {e.orderId && e.kind === "Used" && (
                                <>
                                    {" on "}
                                    <Link
                                        to={`/orders/${e.orderId}`}
                                        className="underline-offset-2 hover:underline"
                                    >
                                        {e.orderNumber}
                                    </Link>
                                </>
                            )}
                            {e.text && (
                                <span className="block text-muted-foreground">
                                    {e.text}
                                </span>
                            )}
                            <span className="block text-xs text-muted-foreground">
                                {dateTime(e.atUtc)}
                                {e.by && ` · ${e.by}`}
                            </span>
                        </span>
                        <span
                            className={cn(
                                "text-right tabular-nums",
                                e.quantityCft < 0 && "text-muted-foreground",
                            )}
                        >
                            {e.quantityCft > 0 ? "+" : ""}
                            {cft(e.quantityCft)}
                        </span>
                    </li>
                ))}
            </ol>

            {canManage && (
                <footer className="space-y-3 border-t px-4 py-3">
                    {open === null && (
                        <div className="flex flex-wrap gap-2">
                            <Button
                                variant="outline"
                                className="h-11 px-4"
                                onClick={() => setOpen("correct")}
                            >
                                Correct
                            </Button>
                            {credit.balanceCft > 0 &&
                                credit.pendingCloseCft === 0 && (
                                    <Button
                                        variant="outline"
                                        className="h-11 px-4"
                                        onClick={() => setOpen("close")}
                                    >
                                        Close credit
                                    </Button>
                                )}
                        </div>
                    )}
                    {open === "correct" && (
                        <CorrectForm
                            pending={act.isPending}
                            onCancel={done}
                            onSave={(body) =>
                                act.mutate(
                                    {
                                        url: `/api/ledger/credits/${credit.id}/corrections`,
                                        body,
                                    },
                                    { onSuccess: done },
                                )
                            }
                        />
                    )}
                    {open === "close" && (
                        <CloseForm
                            balance={credit.balanceCft}
                            pending={act.isPending}
                            onCancel={done}
                            onSave={(body) =>
                                act.mutate(
                                    {
                                        url: `/api/ledger/credits/${credit.id}/closes`,
                                        body,
                                    },
                                    { onSuccess: done },
                                )
                            }
                        />
                    )}
                    {act.isError && (
                        <p role="alert" className="text-sm text-late">
                            {act.error.message}
                        </p>
                    )}
                </footer>
            )}
        </article>
    );
}

function CorrectForm({
    pending,
    onCancel,
    onSave,
}: {
    pending: boolean;
    onCancel: () => void;
    onSave: (body: { quantityCft: number; reason: string }) => void;
}) {
    const [direction, setDirection] = useState<"add" | "remove">("remove");
    const [quantity, setQuantity] = useState("");
    const [reason, setReason] = useState("");
    const value = parseAmount(quantity);
    const ok =
        value !== null &&
        value > 0 &&
        twoDecimals(value) &&
        reason.trim().length >= 3;

    return (
        <div className="space-y-3 rounded-sm border bg-muted/30 p-3">
            <div
                className="flex flex-wrap gap-2"
                role="radiogroup"
                aria-label="Correction"
            >
                {(["remove", "add"] as const).map((d) => (
                    <button
                        key={d}
                        type="button"
                        role="radio"
                        aria-checked={direction === d}
                        onClick={() => setDirection(d)}
                        className={cn(
                            "h-11 rounded-sm border px-3 text-sm",
                            direction === d
                                ? "border-ink bg-ink text-white"
                                : "bg-card hover:border-ink",
                        )}
                    >
                        {d === "remove" ? "Take CFT away" : "Add CFT"}
                    </button>
                ))}
            </div>
            <div className="grid gap-3 sm:grid-cols-[10rem_1fr]">
                <Field label="CFT">
                    <input
                        inputMode="decimal"
                        value={quantity}
                        onChange={(e) => setQuantity(e.target.value)}
                        className={inputClass}
                    />
                </Field>
                <Field label="Why?">
                    <input
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        maxLength={500}
                        className={inputClass}
                    />
                </Field>
            </div>
            <div className="flex flex-wrap gap-2">
                <Button
                    className="h-11 px-4"
                    disabled={!ok || pending}
                    onClick={() =>
                        onSave({
                            quantityCft:
                                direction === "remove" ? -value! : value!,
                            reason: reason.trim(),
                        })
                    }
                >
                    Save correction
                </Button>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={onCancel}
                >
                    Cancel
                </Button>
            </div>
        </div>
    );
}

function CloseForm({
    balance,
    pending,
    onCancel,
    onSave,
}: {
    balance: number;
    pending: boolean;
    onCancel: () => void;
    onSave: (body: {
        quantityCft: number;
        reason: LedgerCloseReason;
        note: string;
    }) => void;
}) {
    const [reason, setReason] = useState<LedgerCloseReason>("Refunded");
    const [quantity, setQuantity] = useState(String(balance));
    const [note, setNote] = useState("");
    const value = parseAmount(quantity);
    const ok =
        value !== null &&
        value > 0 &&
        value <= balance &&
        twoDecimals(value) &&
        note.trim().length >= 3;

    return (
        <div className="space-y-3 rounded-sm border bg-muted/30 p-3">
            <p className="text-sm text-muted-foreground">
                Someone with approval rights has to agree before the credit
                closes. Until then this volume can't be used.
            </p>
            <div
                className="flex flex-wrap gap-2"
                role="radiogroup"
                aria-label="Why close it"
            >
                {(Object.keys(closeReasonLabel) as LedgerCloseReason[]).map(
                    (r) => (
                        <button
                            key={r}
                            type="button"
                            role="radio"
                            aria-checked={reason === r}
                            onClick={() => setReason(r)}
                            className={cn(
                                "h-11 rounded-sm border px-3 text-sm",
                                reason === r
                                    ? "border-ink bg-ink text-white"
                                    : "bg-card hover:border-ink",
                            )}
                        >
                            {closeReasonLabel[r]}
                        </button>
                    ),
                )}
            </div>
            <div className="grid gap-3 sm:grid-cols-[10rem_1fr]">
                <Field label="CFT to close" hint={`${cft(balance)} left`}>
                    <input
                        inputMode="decimal"
                        value={quantity}
                        onChange={(e) => setQuantity(e.target.value)}
                        className={inputClass}
                    />
                </Field>
                <Field label="Note" hint="For example how the refund was paid.">
                    <input
                        value={note}
                        onChange={(e) => setNote(e.target.value)}
                        maxLength={500}
                        className={inputClass}
                    />
                </Field>
            </div>
            <div className="flex flex-wrap gap-2">
                <Button
                    className="h-11 px-4"
                    disabled={!ok || pending}
                    onClick={() =>
                        onSave({
                            quantityCft: value!,
                            reason,
                            note: note.trim(),
                        })
                    }
                >
                    Ask to close
                </Button>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={onCancel}
                >
                    Cancel
                </Button>
            </div>
        </div>
    );
}
