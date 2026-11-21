import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { LuChevronLeft, LuChevronRight, LuTriangleAlert } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { parseAmount } from "@/components/orders/orderDraft";
import { inputClass, panelClass } from "@/components/orders/styles";
import {
    useRemoveTarget,
    useSetTarget,
    useTargetQuarter,
} from "@/components/targets/useTargets";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import {
    nextQuarter,
    percentOf,
    previousQuarter,
    type TargetQuarter,
    type TargetRow,
} from "@/lib/targets";
import { cn } from "@/lib/utils";

const longDate = (date: string) =>
    new Date(`${date}T12:00:00`).toLocaleDateString([], {
        day: "numeric",
        month: "short",
        year: "numeric",
    });

// Quarterly volume targets for the company, each plant and each sales rep, against billed volume.
// A sales rep sees only their own target.
export function Targets() {
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.targetsManage);
    const [params, setParams] = useSearchParams();
    const year = Number(params.get("year"));
    const quarter = Number(params.get("quarter"));
    const at = year > 0 && quarter > 0 ? { year, quarter } : null;
    const data = useTargetQuarter(at);
    const q = data.data;

    const go = (to: { year: number; quarter: number }) =>
        setParams({ year: String(to.year), quarter: String(to.quarter) });

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Targets</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    The volume to bill each quarter. Billed counts orders closed
                    in the quarter. Provisional counts what has been delivered
                    on orders still open, and only shows for the quarter running
                    now.
                </p>
            </header>

            {data.isLoading && <Skeleton className="h-60 w-full" />}
            {data.isError && (
                <p role="alert" className="text-sm text-late">
                    {data.error.message}
                </p>
            )}

            {q && (
                <>
                    <QuarterBar
                        q={q}
                        onPrevious={() =>
                            go(previousQuarter(q.year, q.quarter))
                        }
                        onNext={() => go(nextQuarter(q.year, q.quarter))}
                        onToday={() => setParams({})}
                    />

                    {q.warnings.length > 0 && (
                        <ul
                            role="status"
                            className={cn(
                                panelClass,
                                "space-y-1 border-ink/40 p-3 text-sm",
                            )}
                        >
                            {q.warnings.map((w) => (
                                <li key={w} className="flex gap-2">
                                    <LuTriangleAlert
                                        aria-hidden
                                        className="mt-0.5 size-4 shrink-0"
                                    />
                                    {w}
                                </li>
                            ))}
                        </ul>
                    )}

                    {q.company ? (
                        <TargetCard
                            row={q.company}
                            q={q}
                            canEdit={canManage && !q.ended}
                            large
                        />
                    ) : (
                        <MyTarget q={q} />
                    )}

                    {q.company && (
                        <>
                            <TargetSection
                                title="Plants"
                                rows={q.plants}
                                q={q}
                                canEdit={canManage && !q.ended}
                                empty="No plants yet."
                            />
                            <TargetSection
                                title="Sales reps"
                                rows={q.reps}
                                q={q}
                                canEdit={canManage && !q.ended}
                                empty="No sales reps yet. Reps are added on the Sales reps screen."
                            />
                        </>
                    )}
                </>
            )}
        </div>
    );
}

function QuarterBar({
    q,
    onPrevious,
    onNext,
    onToday,
}: {
    q: TargetQuarter;
    onPrevious: () => void;
    onNext: () => void;
    onToday: () => void;
}) {
    const state = q.current
        ? "This quarter"
        : q.ended
          ? "Ended, targets are fixed"
          : "Not started yet";

    return (
        <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center gap-2">
                <Button
                    variant="outline"
                    className="size-11 p-0"
                    aria-label="Previous quarter"
                    onClick={onPrevious}
                >
                    <LuChevronLeft aria-hidden className="size-5" />
                </Button>
                <div className="min-w-44 text-center">
                    <p className="display text-2xl leading-none">{q.label}</p>
                    <p className="text-xs text-muted-foreground">
                        {longDate(q.startDate)} – {longDate(q.endDate)}
                    </p>
                </div>
                <Button
                    variant="outline"
                    className="size-11 p-0"
                    aria-label="Next quarter"
                    onClick={onNext}
                >
                    <LuChevronRight aria-hidden className="size-5" />
                </Button>
            </div>
            <span
                className={cn(
                    "rounded-sm border px-2 py-1 text-xs font-semibold",
                    q.current
                        ? "border-ok/40 bg-ok/10 text-ok"
                        : "text-muted-foreground",
                )}
            >
                {state}
            </span>
            {!q.current && (
                <button
                    type="button"
                    onClick={onToday}
                    className="min-h-11 text-sm font-medium underline-offset-2 hover:underline"
                >
                    Back to this quarter
                </button>
            )}
        </div>
    );
}

// What a sales rep sees: their own target only.
function MyTarget({ q }: { q: TargetQuarter }) {
    if (q.reps.length === 0) {
        return (
            <p className={cn(panelClass, "p-4 text-sm text-muted-foreground")}>
                Your login isn't linked to a sales rep, so there is no target to
                show. Ask your sales manager to link it on the Sales reps
                screen.
            </p>
        );
    }
    return <TargetCard row={q.reps[0]} q={q} canEdit={false} large />;
}

function TargetSection({
    title,
    rows,
    q,
    canEdit,
    empty,
}: {
    title: string;
    rows: TargetRow[];
    q: TargetQuarter;
    canEdit: boolean;
    empty: string;
}) {
    const set = rows.filter((r) => r.targetCft !== null).length;
    return (
        <section className="space-y-3" aria-label={title}>
            <div className="flex flex-wrap items-baseline justify-between gap-2">
                <h2 className="display text-2xl">{title}</h2>
                {rows.length > 0 && (
                    <p className="text-sm text-muted-foreground">
                        {set} of {rows.length} with a target
                    </p>
                )}
            </div>
            {rows.length === 0 ? (
                <p
                    className={cn(
                        panelClass,
                        "p-4 text-sm text-muted-foreground",
                    )}
                >
                    {empty}
                </p>
            ) : (
                <div className="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-3">
                    {rows.map((r) => (
                        <TargetCard
                            key={r.plantId ?? r.salesRepId}
                            row={r}
                            q={q}
                            canEdit={canEdit}
                        />
                    ))}
                </div>
            )}
        </section>
    );
}

function TargetCard({
    row,
    q,
    canEdit,
    large = false,
}: {
    row: TargetRow;
    q: TargetQuarter;
    canEdit: boolean;
    large?: boolean;
}) {
    const [editing, setEditing] = useState(false);
    const target = row.targetCft;
    const reached = percentOf(row.billedCft, target);
    const withOpen = percentOf(row.billedCft + row.provisionalCft, target);

    return (
        <article className={cn(panelClass, "space-y-3 p-4")}>
            <header className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                    <h3
                        className={cn(
                            "font-semibold",
                            large && "display text-2xl font-normal",
                        )}
                    >
                        {row.code && (
                            <span className="mr-1.5 text-muted-foreground">
                                {row.code}
                            </span>
                        )}
                        {row.name}
                    </h3>
                    <p className="text-sm text-muted-foreground tabular-nums">
                        {target === null
                            ? "No target set"
                            : `Target ${cft(target)} CFT`}
                    </p>
                </div>
                {reached !== null && (
                    <p
                        className={cn(
                            "display shrink-0 leading-none tabular-nums",
                            large ? "text-4xl" : "text-3xl",
                            reached >= 100 && "text-ok",
                        )}
                    >
                        {reached}%
                    </p>
                )}
            </header>

            <Progress row={row} />

            <dl className="flex flex-wrap gap-x-5 gap-y-1 text-sm">
                <div>
                    <dt className="text-xs text-muted-foreground">Billed</dt>
                    <dd className="font-semibold tabular-nums">
                        {cft(row.billedCft)} CFT
                    </dd>
                </div>
                {q.current && (
                    <div>
                        <dt className="text-xs text-muted-foreground">
                            Provisional
                        </dt>
                        <dd className="tabular-nums">
                            {cft(row.provisionalCft)} CFT
                            {withOpen !== null && row.provisionalCft > 0 && (
                                <span className="text-muted-foreground">
                                    {" "}
                                    ({withOpen}% with it)
                                </span>
                            )}
                        </dd>
                    </div>
                )}
                {target !== null && (
                    <div>
                        <dt className="text-xs text-muted-foreground">
                            Still to bill
                        </dt>
                        <dd className="tabular-nums">
                            {cft(Math.max(0, target - row.billedCft))} CFT
                        </dd>
                    </div>
                )}
            </dl>

            {canEdit &&
                (editing ? (
                    <TargetForm
                        row={row}
                        q={q}
                        onDone={() => setEditing(false)}
                    />
                ) : (
                    <Button
                        variant="outline"
                        className="h-11 px-4"
                        onClick={() => setEditing(true)}
                    >
                        {target === null ? "Set target" : "Change target"}
                    </Button>
                ))}
        </article>
    );
}

// Billed is solid, provisional is lighter on top of it. The bar is full at the target.
function Progress({ row }: { row: TargetRow }) {
    const target = row.targetCft;
    if (!target) return null;
    const billed = Math.min(1, row.billedCft / target);
    const open = Math.min(1 - billed, row.provisionalCft / target);
    return (
        <div
            className="flex h-2.5 overflow-hidden rounded-full bg-muted"
            role="img"
            aria-label={`${cft(row.billedCft)} of ${cft(target)} CFT billed`}
        >
            <div className="bg-ink" style={{ width: `${billed * 100}%` }} />
            <div className="bg-ink/30" style={{ width: `${open * 100}%` }} />
        </div>
    );
}

function TargetForm({
    row,
    q,
    onDone,
}: {
    row: TargetRow;
    q: TargetQuarter;
    onDone: () => void;
}) {
    const set = useSetTarget();
    const remove = useRemoveTarget();
    const [value, setValue] = useState(
        row.targetCft === null ? "" : String(row.targetCft),
    );
    const volume = parseAmount(value);
    const ok =
        volume !== null &&
        volume > 0 &&
        Math.round(volume * 100) === volume * 100;
    const error = set.error ?? remove.error;

    return (
        <div className="space-y-3 rounded-sm border bg-muted/30 p-3">
            <label className="block space-y-1 text-sm font-medium">
                Target for {q.label} (CFT)
                <input
                    inputMode="decimal"
                    value={value}
                    onChange={(e) => setValue(e.target.value)}
                    className={cn(inputClass, "h-11")}
                    autoFocus
                />
            </label>
            <div className="flex flex-wrap gap-2">
                <Button
                    className="h-11 px-4"
                    disabled={!ok || set.isPending}
                    onClick={() =>
                        set.mutate(
                            {
                                year: q.year,
                                quarter: q.quarter,
                                level: row.level,
                                plantId: row.plantId,
                                salesRepId: row.salesRepId,
                                volumeCft: volume!,
                            },
                            { onSuccess: onDone },
                        )
                    }
                >
                    Save target
                </Button>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={onDone}
                >
                    Cancel
                </Button>
                {row.targetId && (
                    <Button
                        variant="outline"
                        tone="danger"
                        className="h-11 px-4"
                        disabled={remove.isPending}
                        onClick={() =>
                            remove.mutate(row.targetId!, { onSuccess: onDone })
                        }
                    >
                        Remove
                    </Button>
                )}
            </div>
            {error && (
                <p role="alert" className="text-sm text-late">
                    {error.message}
                </p>
            )}
        </div>
    );
}
