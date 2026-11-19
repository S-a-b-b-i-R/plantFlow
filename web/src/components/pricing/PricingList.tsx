import { Fragment, useDeferredValue, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { LuPlus, LuSearch } from "react-icons/lu";
import { can } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { ReasonAction } from "@/components/orders/ReasonAction";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import type { Paged } from "@/lib/orders";
import {
    lifecycleLabel,
    lifecycleOf,
    lifecycles,
    type Lifecycle,
} from "@/lib/pricing";
import { cn } from "@/lib/utils";
import type { PriceBookKind, PriceBookRow } from "./kind";
import { LifecycleChip } from "./LifecycleChip";
import { useBookAct } from "./useBook";

// One row with the actions its stage allows to this person.
export function BookRow({
    kind,
    row,
    onEdit,
}: {
    kind: PriceBookKind;
    row: PriceBookRow;
    onEdit?: (row: PriceBookRow) => void;
}) {
    const me = useMe().data!;
    const act = useBookAct();
    const [confirm, setConfirm] = useState<"end" | "delete" | null>(null);
    const [showDetail, setShowDetail] = useState(false);

    const life = lifecycleOf(row.status);
    const canEdit = can(me, kind.edit);
    const canApprove = can(me, kind.approve);
    const base = `${kind.endpoint}/${row.id}`;
    const post = (path: string, body?: unknown) =>
        act.mutate({ url: `${base}/${path}`, body });

    return (
        <li className="space-y-2 px-4 py-3">
            <div
                className={cn(
                    "grid gap-x-3 gap-y-1 md:items-center",
                    kind.grid,
                )}
            >
                {kind.cells(row).map((cell, index) => (
                    <Fragment key={index}>{cell}</Fragment>
                ))}
                <LifecycleChip status={row.status} />
            </div>

            {life === "draft" && row.rejectionReason && (
                <p className="text-sm text-late">
                    Sent back: {row.rejectionReason}
                </p>
            )}
            {life === "pending" && !canApprove && (
                <p className="text-sm text-muted-foreground">
                    Waiting for management to approve it.
                </p>
            )}

            <div className="flex flex-wrap items-center gap-2">
                {life === "draft" && canEdit && (
                    <>
                        <Button
                            className={cn("h-10 px-4", nextButtonClass)}
                            disabled={act.isPending}
                            onClick={() => post("submit")}
                        >
                            Submit for approval
                        </Button>
                        <Button
                            variant="outline"
                            className="h-10 px-4"
                            onClick={() => onEdit?.(row)}
                        >
                            Edit
                        </Button>
                        {kind.deletable && confirm !== "delete" && (
                            <Button
                                variant="outline"
                                tone="danger"
                                className="h-10 px-4"
                                onClick={() => setConfirm("delete")}
                            >
                                Delete
                            </Button>
                        )}
                        {confirm === "delete" && (
                            <Confirm
                                question={`Delete this ${kind.noun}?`}
                                yes="Delete"
                                pending={act.isPending}
                                onYes={() => {
                                    act.mutate({ url: base, method: "DELETE" });
                                    setConfirm(null);
                                }}
                                onNo={() => setConfirm(null)}
                            />
                        )}
                    </>
                )}

                {life === "pending" && canApprove && (
                    <>
                        <Button
                            className={cn("h-10 px-4", nextButtonClass)}
                            disabled={act.isPending}
                            onClick={() => post("approve")}
                        >
                            Approve
                        </Button>
                        <ReasonAction
                            label="Send back"
                            question="Why is it being sent back?"
                            submitLabel="Send back"
                            pending={act.isPending}
                            onSubmit={(reason) => post("reject", { reason })}
                        />
                    </>
                )}

                {life === "live" && canApprove && confirm !== "end" && (
                    <Button
                        variant="outline"
                        className="h-10 px-4"
                        onClick={() => setConfirm("end")}
                    >
                        {kind.endLabel}
                    </Button>
                )}
                {confirm === "end" && (
                    <Confirm
                        question={`${kind.endLabel} this ${kind.noun}? Price checks stop using it right away.`}
                        yes={kind.endLabel}
                        pending={act.isPending}
                        onYes={() => {
                            post(kind.endPath);
                            setConfirm(null);
                        }}
                        onNo={() => setConfirm(null)}
                    />
                )}

                {kind.Detail && (
                    <Button
                        variant="ghost"
                        className="h-10 px-3"
                        onClick={() => setShowDetail(!showDetail)}
                    >
                        {showDetail ? "Hide materials" : "Show materials"}
                    </Button>
                )}
            </div>

            {showDetail && kind.Detail && <kind.Detail row={row} />}

            {act.isError && (
                <p className="text-sm font-medium text-late" role="alert">
                    {act.error.message}
                </p>
            )}
        </li>
    );
}

function Confirm({
    question,
    yes,
    pending,
    onYes,
    onNo,
}: {
    question: string;
    yes: string;
    pending: boolean;
    onYes: () => void;
    onNo: () => void;
}) {
    return (
        <span className="flex flex-wrap items-center gap-2 rounded-sm border bg-muted/40 px-3 py-1.5 text-sm">
            {question}
            <Button
                tone="danger"
                className="h-9 px-3"
                disabled={pending}
                onClick={onYes}
            >
                {yes}
            </Button>
            <Button variant="outline" className="h-9 px-3" onClick={onNo}>
                Never mind
            </Button>
        </span>
    );
}

// The column headings above the rows. Hidden on a phone, where each row stacks.
function Head({ kind }: { kind: PriceBookKind }) {
    return (
        <div
            className={cn(
                "hidden gap-3 border-b bg-muted/50 px-4 py-2 text-xs font-medium text-muted-foreground md:grid",
                kind.grid,
            )}
        >
            {kind.head.map((label) => (
                <span key={label}>{label}</span>
            ))}
            <span>Status</span>
        </div>
    );
}

// The full tab for one kind of price book row: filters, the form, and the rows.
export function PricingList({ kind }: { kind: PriceBookKind }) {
    const me = useMe().data!;
    const [search, setSearch] = useState("");
    const [stage, setStage] = useState<Lifecycle | "">("");
    const [page, setPage] = useState(1);
    const [editing, setEditing] = useState<PriceBookRow | "new" | null>(null);
    const deferred = useDeferredValue(search.trim());

    const params = new URLSearchParams({ page: String(page), pageSize: "20" });
    if (deferred) params.set("search", deferred);
    if (stage) params.set("status", kind.statuses[stage]);

    const rows = useQuery({
        queryKey: ["pricebook", kind.key, params.toString()],
        queryFn: () => api<Paged<PriceBookRow>>(`${kind.endpoint}?${params}`),
        placeholderData: (previous) => previous,
    });

    const data = rows.data;
    const first = data ? (data.page - 1) * data.pageSize + 1 : 0;
    const last = data ? first + data.items.length - 1 : 0;
    const pages = data
        ? Math.max(1, Math.ceil(data.totalCount / data.pageSize))
        : 1;
    const canEdit = can(me, kind.edit);

    const change = (apply: () => void) => {
        apply();
        setPage(1);
    };

    return (
        <div className="space-y-4">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div
                    className="flex flex-wrap items-center gap-2"
                    role="group"
                    aria-label="Show"
                >
                    <button
                        type="button"
                        aria-pressed={stage === ""}
                        onClick={() => change(() => setStage(""))}
                        className={chipClass(stage === "")}
                    >
                        All
                    </button>
                    {lifecycles.map((life) => (
                        <button
                            key={life}
                            type="button"
                            aria-pressed={stage === life}
                            onClick={() => change(() => setStage(life))}
                            className={chipClass(stage === life)}
                        >
                            {lifecycleLabel[life]}
                        </button>
                    ))}
                </div>
                {canEdit && editing === null && (
                    <Button
                        className={cn("h-11 gap-2 px-5", nextButtonClass)}
                        onClick={() => setEditing("new")}
                    >
                        <LuPlus aria-hidden /> New {kind.noun}
                    </Button>
                )}
            </div>

            {kind.searchHint && (
                <Field label="Search" className="sm:max-w-md">
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
                            placeholder={kind.searchHint}
                            className={cn(inputClass, "pl-9")}
                        />
                    </span>
                </Field>
            )}

            {editing !== null && (
                <kind.Form
                    key={editing === "new" ? "new" : editing.id}
                    item={editing === "new" ? null : editing}
                    onDone={() => setEditing(null)}
                    onCancel={() => setEditing(null)}
                />
            )}

            {rows.isPending ? (
                <Skeleton className="h-48 w-full" />
            ) : rows.isError ? (
                <p className="text-sm font-medium text-late" role="alert">
                    The list could not be loaded. {rows.error.message}
                </p>
            ) : data!.items.length === 0 ? (
                <p
                    className={cn(
                        panelClass,
                        "px-4 py-12 text-center text-sm text-muted-foreground",
                    )}
                >
                    {deferred || stage
                        ? "Nothing matches. Clear the search or the filter to see more."
                        : canEdit
                          ? `There are no ${kind.label.toLowerCase()} yet. Start with New ${kind.noun}.`
                          : `There are no ${kind.label.toLowerCase()} yet.`}
                </p>
            ) : (
                <section className={cn(panelClass, "overflow-hidden")}>
                    <Head kind={kind} />
                    <ul className="divide-y">
                        {data!.items.map((row) => (
                            <BookRow
                                key={row.id}
                                kind={kind}
                                row={row}
                                onEdit={setEditing}
                            />
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

const chipClass = (active: boolean) =>
    cn(
        "h-9 rounded-sm border px-3 text-sm font-medium",
        active
            ? "border-ink bg-ink text-background"
            : "bg-card hover:border-ink",
    );

// What is waiting for this person in one kind of row. Nothing is drawn when the list is empty.
export function PendingSection({ kind }: { kind: PriceBookKind }) {
    const rows = useQuery({
        queryKey: ["pricebook", kind.key, "pending"],
        queryFn: () =>
            api<Paged<PriceBookRow>>(
                `${kind.endpoint}?status=${kind.statuses.pending}&pageSize=50`,
            ),
    });

    if (rows.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                {kind.label} could not be loaded. {rows.error.message}
            </p>
        );
    }
    if (!rows.data || rows.data.items.length === 0) return null;

    return (
        <section className="space-y-2">
            <h2 className="display text-2xl">{kind.label}</h2>
            <div className={cn(panelClass, "overflow-hidden")}>
                <Head kind={kind} />
                <ul className="divide-y">
                    {rows.data.items.map((row) => (
                        <BookRow key={row.id} kind={kind} row={row} />
                    ))}
                </ul>
            </div>
        </section>
    );
}
