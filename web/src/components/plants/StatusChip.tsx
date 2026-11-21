import type { ActiveStatus } from "@/lib/plants";
import { cn } from "@/lib/utils";

export function StatusChip({ status }: { status: ActiveStatus }) {
    return (
        <span
            className={cn(
                "inline-flex w-fit items-center rounded-sm border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
                status === "Active"
                    ? "border-ok/40 bg-ok/10 text-ok"
                    : "border-border bg-muted text-muted-foreground",
            )}
        >
            {status}
        </span>
    );
}
