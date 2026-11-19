import { statusLabel, statusTone, type OrderStatus } from "@/lib/orders";
import { cn } from "@/lib/utils";

const tones = {
    ok: "border-ok/40 bg-ok/10 text-ok",
    late: "border-late/40 bg-late/10 text-late",
    quiet: "border-border bg-muted text-muted-foreground",
    ink: "border-ink/30 bg-ink/5 text-foreground",
};

export function StatusChip({
    status,
    className,
}: {
    status: OrderStatus;
    className?: string;
}) {
    return (
        <span
            className={cn(
                "inline-flex items-center rounded-sm border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
                tones[statusTone(status)],
                className,
            )}
        >
            {statusLabel[status]}
        </span>
    );
}
