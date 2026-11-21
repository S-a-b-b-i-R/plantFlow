import { useDeferredValue, useState } from "react";
import {
    LuChevronDown,
    LuDownload,
    LuExternalLink,
    LuSearch,
} from "react-icons/lu";
import { Link } from "react-router-dom";
import {
    PAGE_SIZE,
    useAuditExport,
    useAuditFilters,
    useAuditLog,
} from "@/components/audit/useAudit";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { inputClass, panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import {
    actionLabel,
    changeRows,
    emptyAuditQuery,
    recordLink,
    recordTypeLabel,
    type AuditEntry,
    type AuditQuery,
} from "@/lib/audit";
import { cn } from "@/lib/utils";

const when = new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
});

// Every change anyone made, newest first. Nothing here can be edited or deleted.
export function AuditLog() {
    const [query, setQuery] = useState<AuditQuery>(emptyAuditQuery);
    const [page, setPage] = useState(1);
    const [open, setOpen] = useState<string | null>(null);
    const deferred = useDeferredValue(query);
    const log = useAuditLog(deferred, page);
    const filters = useAuditFilters();
    const exporter = useAuditExport();

    const change = (patch: Partial<AuditQuery>) => {
        setQuery((now) => ({ ...now, ...patch }));
        setPage(1);
        setOpen(null);
        exporter.reset();
    };
    const filtered = Object.values(query).some((v) => v.trim());
    const total = log.data?.totalCount ?? 0;
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
    const first = (page - 1) * PAGE_SIZE + 1;
    const last = Math.min(page * PAGE_SIZE, total);

    return (
        <div className="space-y-5">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">Audit log</h1>
                    <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                        Who changed what, and when. Entries are kept for good
                        and can't be edited. Dates are company days; the export
                        shows company time.
                    </p>
                </div>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    disabled={exporter.isPending || total === 0}
                    onClick={() => exporter.mutate(query)}
                >
                    <LuDownload aria-hidden className="size-4" />
                    {exporter.isPending ? "Exporting…" : "Export CSV"}
                </Button>
            </header>
            {exporter.error && (
                <p role="alert" className="text-sm font-medium text-late">
                    {exporter.error.message}
                </p>
            )}

            <div className="grid grid-cols-2 gap-3 lg:grid-cols-[repeat(4,minmax(0,11rem))_minmax(0,1fr)]">
                <Field label="From">
                    <input
                        type="date"
                        value={query.from}
                        max={query.to || undefined}
                        onChange={(e) => change({ from: e.target.value })}
                        className={inputClass}
                    />
                </Field>
                <Field label="To">
                    <input
                        type="date"
                        value={query.to}
                        min={query.from || undefined}
                        onChange={(e) => change({ to: e.target.value })}
                        className={inputClass}
                    />
                </Field>
                <Field label="Person" className="col-span-2 sm:col-span-1">
                    <select
                        value={query.userId}
                        onChange={(e) => change({ userId: e.target.value })}
                        className={inputClass}
                    >
                        <option value="">Anyone</option>
                        {filters.data?.people.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="Record" className="col-span-2 sm:col-span-1">
                    <select
                        value={query.entityType}
                        onChange={(e) => change({ entityType: e.target.value })}
                        className={inputClass}
                    >
                        <option value="">Any record</option>
                        {filters.data?.entityTypes.map((t) => (
                            <option key={t} value={t}>
                                {recordTypeLabel(t)}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="Search" className="col-span-2 lg:col-span-1">
                    <span className="relative block">
                        <LuSearch
                            aria-hidden
                            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                        />
                        <input
                            type="search"
                            value={query.search}
                            onChange={(e) => change({ search: e.target.value })}
                            placeholder="Action, record id or a changed value"
                            className={cn(inputClass, "pl-9")}
                        />
                    </span>
                </Field>
            </div>

            {log.isLoading && <Skeleton className="h-64 w-full" />}
            {log.isError && (
                <p role="alert" className="text-sm text-late">
                    {log.error.message}
                </p>
            )}
            {log.data && total === 0 && (
                <p className="text-sm text-muted-foreground">
                    {filtered
                        ? "Nothing matches these filters."
                        : "Nothing has been recorded yet."}{" "}
                    {filtered && (
                        <button
                            type="button"
                            className="font-medium text-foreground underline underline-offset-2"
                            onClick={() => change(emptyAuditQuery)}
                        >
                            Clear filters
                        </button>
                    )}
                </p>
            )}

            {log.data && total > 0 && (
                <>
                    <div className={cn(panelClass, "overflow-hidden")}>
                        <div className="hidden grid-cols-[11rem_12rem_1fr_14rem_1.25rem] gap-4 border-b bg-muted/40 px-4 py-2 text-xs font-semibold text-muted-foreground lg:grid">
                            <span>When</span>
                            <span>Who</span>
                            <span>What</span>
                            <span>Record</span>
                            <span />
                        </div>
                        <ul
                            className={cn(
                                "divide-y",
                                log.isPlaceholderData && "opacity-60",
                            )}
                        >
                            {log.data.items.map((entry) => (
                                <EntryRow
                                    key={entry.id}
                                    entry={entry}
                                    open={open === entry.id}
                                    onToggle={() =>
                                        setOpen((now) =>
                                            now === entry.id ? null : entry.id,
                                        )
                                    }
                                />
                            ))}
                        </ul>
                    </div>
                    <nav
                        aria-label="Pages"
                        className="flex flex-wrap items-center justify-between gap-3"
                    >
                        <span className="text-sm text-muted-foreground tabular-nums">
                            {first.toLocaleString()}–{last.toLocaleString()} of{" "}
                            {total.toLocaleString()}
                        </span>
                        <span className="flex gap-2">
                            <Button
                                variant="outline"
                                className="h-11 px-4"
                                disabled={page <= 1}
                                onClick={() => {
                                    setPage((p) => p - 1);
                                    setOpen(null);
                                }}
                            >
                                Newer
                            </Button>
                            <Button
                                variant="outline"
                                className="h-11 px-4"
                                disabled={page >= pages}
                                onClick={() => {
                                    setPage((p) => p + 1);
                                    setOpen(null);
                                }}
                            >
                                Older
                            </Button>
                        </span>
                    </nav>
                </>
            )}
        </div>
    );
}

function EntryRow({
    entry,
    open,
    onToggle,
}: {
    entry: AuditEntry;
    open: boolean;
    onToggle: () => void;
}) {
    const who =
        entry.userName ??
        (entry.userId ? "Someone outside the company" : "PlantFlow");
    const link = recordLink(entry.entityType, entry.entityId);
    const rows = changeRows(entry.changesJson);
    const panelId = `audit-${entry.id}`;

    return (
        <li>
            <button
                type="button"
                onClick={onToggle}
                disabled={rows.length === 0}
                aria-expanded={rows.length > 0 ? open : undefined}
                aria-controls={panelId}
                className="grid w-full grid-cols-[1fr_auto] gap-x-4 gap-y-0.5 px-4 py-3 text-left text-sm enabled:hover:bg-muted/40 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-ink lg:grid-cols-[11rem_12rem_1fr_14rem_1.25rem] lg:items-center"
            >
                <span className="text-muted-foreground tabular-nums lg:text-foreground">
                    {when.format(new Date(entry.occurredAtUtc))}
                </span>
                <span className="col-start-1 truncate lg:col-start-auto">
                    {who}
                </span>
                <span className="col-start-1 font-medium lg:col-start-auto">
                    {actionLabel(entry.action)}
                </span>
                <span className="col-start-1 min-w-0 truncate text-muted-foreground lg:col-start-auto">
                    {recordTypeLabel(entry.entityType)}{" "}
                    <span className="font-mono text-xs">{entry.entityId}</span>
                </span>
                <span className="col-start-2 row-span-4 row-start-1 self-center lg:col-start-auto lg:row-span-1 lg:row-start-auto">
                    {rows.length > 0 && (
                        <LuChevronDown
                            aria-hidden
                            className={cn(
                                "size-5 text-muted-foreground transition-transform motion-reduce:transition-none",
                                open && "rotate-180",
                            )}
                        />
                    )}
                </span>
            </button>
            {open && (
                <div
                    id={panelId}
                    className="space-y-3 border-t bg-muted/20 px-4 py-3"
                >
                    <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[minmax(8rem,auto)_1fr]">
                        {rows.map(([label, value]) => (
                            <div key={label} className="contents">
                                <dt className="text-muted-foreground">
                                    {label}
                                </dt>
                                <dd className="mb-2 break-words sm:mb-0">
                                    {value}
                                </dd>
                            </div>
                        ))}
                    </dl>
                    {link && (
                        <Link
                            to={link}
                            className="inline-flex min-h-11 items-center gap-1.5 text-sm font-medium underline underline-offset-2"
                        >
                            Open this{" "}
                            {recordTypeLabel(entry.entityType).toLowerCase()}
                            <LuExternalLink aria-hidden className="size-4" />
                        </Link>
                    )}
                </div>
            )}
        </li>
    );
}
