import {
    cft,
    clock,
    clockAt,
    loadParts,
    plantTime,
    signedMinutes,
} from "@/lib/format";
import {
    place,
    planAxis,
    stepRows,
    ticks,
    type Axis,
    type StepRow,
} from "@/lib/timeline";
import type { LivePlan, LivePour } from "@/lib/types";

// Pour info, step names, then the timeline. The overlay below starts after the first two (15rem + 9rem).
const columns = "grid-cols-[15rem_9rem_1fr]";

export function PlanPanel({ plan, now }: { plan: LivePlan; now: number }) {
    const approved = plan.status === "Approved";
    const status = approved
        ? "Approved"
        : plan.status === "Submitted"
          ? "Waiting for approval"
          : "Draft";
    const axis = planAxis(plan, now);
    const nowPlace = axis ? place(axis, now) : null;
    const nowPct = nowPlace && nowPlace.outside === null ? nowPlace.pct : null;

    return (
        <section className="overflow-hidden rounded-sm border bg-card">
            <header className="flex flex-wrap items-end justify-between gap-3 border-b px-5 py-4">
                <div>
                    <h2 className="display text-4xl leading-none">
                        {plan.plantName}
                    </h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                        {plan.shiftName} shift
                    </p>
                </div>
                <div className="text-right text-sm">
                    <p className="inline-flex items-center gap-2 font-medium">
                        <span
                            className={`size-2.5 ${approved ? "bg-ok" : "bg-muted-foreground"}`}
                            aria-hidden="true"
                        />
                        {status}
                    </p>
                    <p className="text-muted-foreground">
                        {cft(plan.producedCft)} of {cft(plan.plannedCft)} CFT
                        produced, {cft(plan.remainingCft)} to go
                    </p>
                </div>
            </header>

            {!approved && (
                <p className="border-b px-5 py-2 text-sm text-muted-foreground">
                    Delays start counting once the plant manager approves this
                    plan.
                </p>
            )}

            {axis === null ? (
                <p className="px-5 py-6 text-sm text-muted-foreground">
                    No pours are planned in this shift yet.
                </p>
            ) : (
                <div className="overflow-x-auto overflow-y-hidden">
                    <div className="relative min-w-240 overflow-x-clip">
                        <div className="pointer-events-none absolute inset-y-0 left-96 right-0">
                            {ticks(axis).map((t) => (
                                <div
                                    key={t}
                                    className="absolute inset-y-0 w-px bg-border"
                                    style={{ left: `${place(axis, t).pct}%` }}
                                />
                            ))}
                            {nowPct !== null && (
                                <div
                                    className="absolute inset-y-0 z-20 w-0.5 bg-signal"
                                    style={{ left: `${nowPct}%` }}
                                >
                                    <span
                                        className={`absolute top-0 bg-signal px-1.5 text-xs font-bold text-ink ${
                                            nowPct > 95
                                                ? "right-0"
                                                : "left-1/2 -translate-x-1/2"
                                        }`}
                                    >
                                        Now
                                    </span>
                                </div>
                            )}
                        </div>

                        <div
                            className={`grid ${columns} h-9`}
                            aria-hidden="true"
                        >
                            <div />
                            <div />
                            <div className="relative">
                                {ticks(axis).map((t) => {
                                    const pct = place(axis, t).pct;
                                    return (
                                        <span
                                            key={t}
                                            className={`absolute bottom-1 whitespace-nowrap text-xs tabular-nums text-muted-foreground ${
                                                pct > 95
                                                    ? "-translate-x-full"
                                                    : "-translate-x-1/2"
                                            }`}
                                            style={{ left: `${pct}%` }}
                                        >
                                            {clockAt(t)}
                                        </span>
                                    );
                                })}
                            </div>
                        </div>

                        {plan.pours.map((pour) => (
                            <PourBlock
                                key={pour.pourId}
                                pour={pour}
                                axis={axis}
                                now={now}
                            />
                        ))}
                    </div>
                </div>
            )}
        </section>
    );
}

function PourBlock({
    pour,
    axis,
    now,
}: {
    pour: LivePour;
    axis: Axis;
    now: number;
}) {
    const rows = stepRows(pour);

    return (
        <div
            className={`grid ${columns} border-t border-l-4 ${pour.late ? "border-l-late" : "border-l-transparent"}`}
        >
            <PourInfo pour={pour} />

            <div className="flex flex-col justify-center py-3">
                {rows.map((row) => (
                    <div
                        key={row.key}
                        className="flex h-6 items-center text-xs text-muted-foreground"
                    >
                        {row.label}
                    </div>
                ))}
            </div>

            <div className="flex flex-col justify-center py-3">
                {rows.map((row) => (
                    <Lane key={row.key} row={row} axis={axis} now={now} />
                ))}
            </div>
        </div>
    );
}

function PourInfo({ pour }: { pour: LivePour }) {
    const planned = pour.plannedCft || 1;
    const delivered = Math.min(pour.deliveredCft, planned);
    const onTheWay = Math.max(
        0,
        Math.min(pour.producedCft - pour.deliveredCft, planned - delivered),
    );
    const width = (value: number) => `${(value / planned) * 100}%`;
    const parts = loadParts(pour.loads);

    return (
        <div className="px-4 py-3">
            <div className="flex items-baseline gap-2">
                <p className="display text-3xl leading-none tabular-nums">
                    {plantTime(pour.plannedStart)}
                </p>
                {pour.late && (
                    <p className="text-xs font-semibold text-late">
                        Running late
                    </p>
                )}
            </div>
            <p className="mt-1.5 font-medium leading-tight">
                {pour.customerName}
            </p>
            <p className="text-sm text-muted-foreground">
                {pour.siteName}
                {pour.pumpId ? ", pumped" : ", no pump"}
            </p>

            <div className="mt-3 flex h-2 overflow-hidden rounded-full bg-muted">
                <div
                    className="bg-foreground"
                    style={{ width: width(delivered) }}
                />
                <div
                    className="text-foreground/60"
                    style={{
                        width: width(onTheWay),
                        backgroundImage:
                            "repeating-linear-gradient(45deg, currentColor 0 2px, transparent 2px 5px)",
                    }}
                />
            </div>
            <p className="mt-1.5 text-xs tabular-nums text-muted-foreground">
                {cft(pour.deliveredCft)} of {cft(pour.plannedCft)} CFT delivered
            </p>
            <p className="text-xs text-muted-foreground">
                {parts.length ? parts.join(", ") : "No trucks out"}
            </p>
        </div>
    );
}

// One step on the shift's time axis: a ring at the planned time, a dot where it happened,
// and a line between them that is red when late. A step still waiting runs a dashed line up to now.
function Lane({ row, axis, now }: { row: StepRow; axis: Axis; now: number }) {
    const { step } = row;
    const planned = place(axis, Date.parse(step.plannedUtc));
    const actualMs = step.actualUtc ? Date.parse(step.actualUtc) : null;
    const overdue = actualMs === null && step.delayMinutes !== null;
    const reachMs = actualMs ?? (overdue ? now : null);
    const reach = reachMs === null ? null : place(axis, reachMs);

    const left = Math.min(planned.pct, reach?.pct ?? planned.pct);
    const right = Math.max(planned.pct, reach?.pct ?? planned.pct);
    const late = step.late;
    const label = describe(row.label, step);

    const delayText =
        step.delayMinutes === null
            ? null
            : actualMs === null
              ? `${step.delayMinutes} min overdue`
              : signedMinutes(step.delayMinutes);

    return (
        <div className="relative z-10 h-6" title={label}>
            <span className="sr-only">{label}</span>
            <div
                className="absolute inset-x-0 top-1/2 h-px bg-border"
                aria-hidden="true"
            />

            {reach && right > left && (
                <div
                    className={`absolute top-1/2 h-0.5 -translate-y-1/2 ${overdue ? "" : late ? "bg-late" : "bg-ok"}`}
                    style={{
                        left: `${left}%`,
                        width: `${right - left}%`,
                        backgroundImage: overdue
                            ? "repeating-linear-gradient(90deg, var(--late) 0 6px, transparent 6px 10px)"
                            : undefined,
                    }}
                    aria-hidden="true"
                />
            )}

            <span
                className={`absolute top-1/2 size-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-muted-foreground bg-card ${
                    planned.outside ? "opacity-50" : ""
                }`}
                style={{ left: `${planned.pct}%` }}
                aria-hidden="true"
            />

            {actualMs !== null && reach && (
                <span
                    className={`absolute top-1/2 size-3 -translate-x-1/2 -translate-y-1/2 rounded-full ${late ? "bg-late" : "bg-ok"} ${
                        reach.outside ? "opacity-50" : ""
                    }`}
                    style={{ left: `${reach.pct}%` }}
                    aria-hidden="true"
                />
            )}

            {overdue && reach && (
                <span
                    className="absolute top-1/2 h-3.5 w-0.5 -translate-x-1/2 -translate-y-1/2 bg-late"
                    style={{ left: `${reach.pct}%` }}
                    aria-hidden="true"
                />
            )}

            {delayText && (
                <span
                    className={`absolute top-1/2 -translate-y-1/2 whitespace-nowrap text-xs tabular-nums ${
                        late
                            ? "font-semibold text-late"
                            : "text-muted-foreground"
                    }`}
                    style={
                        right > 80
                            ? { right: `calc(${100 - left}% + 14px)` }
                            : { left: `calc(${right}% + 14px)` }
                    }
                    aria-hidden="true"
                >
                    {delayText}
                </span>
            )}
        </div>
    );
}

function describe(label: string, step: StepRow["step"]) {
    const planned = `planned ${clock(step.plannedUtc)}`;
    if (step.actualUtc) {
        return `${label}: ${planned}, happened ${clock(step.actualUtc)}, ${signedMinutes(step.delayMinutes ?? 0)}`;
    }
    if (step.delayMinutes !== null)
        return `${label}: ${planned}, not yet, ${step.delayMinutes} minutes overdue`;
    return `${label}: ${planned}, not due yet`;
}
