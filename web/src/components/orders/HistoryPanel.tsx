import { panelClass } from "./styles";
import { dateTime, statusLabel, type OrderHistory } from "@/lib/orders";

export function HistoryPanel({ history }: { history: OrderHistory[] }) {
    const sorted = [...history].sort((a, b) =>
        b.changedAtUtc.localeCompare(a.changedAtUtc),
    );
    return (
        <details className={panelClass}>
            <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                History ({history.length})
            </summary>
            <ol className="divide-y border-t">
                {sorted.map((h, i) => (
                    <li
                        key={`${h.changedAtUtc}-${i}`}
                        className="px-4 py-2 text-sm"
                    >
                        <span className="font-medium">
                            {h.fromStatus
                                ? `${statusLabel[h.fromStatus]} to `
                                : "Created as "}
                            {statusLabel[h.toStatus]}
                        </span>
                        <span className="ml-2 text-xs text-muted-foreground">
                            {dateTime(h.changedAtUtc)}
                        </span>
                        {h.reason && (
                            <span className="block text-muted-foreground">
                                “{h.reason}”
                            </span>
                        )}
                    </li>
                ))}
            </ol>
        </details>
    );
}
