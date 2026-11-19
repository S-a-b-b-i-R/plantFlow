import { lifecycleLabel, lifecycleOf } from "@/lib/pricing";
import { cn } from "@/lib/utils";

const tones = {
    draft: "border-border bg-muted text-muted-foreground",
    pending: "border-ink/30 bg-ink/5 text-foreground",
    live: "border-ok/40 bg-ok/10 text-ok",
    ended: "border-border bg-muted text-muted-foreground",
};

export function LifecycleChip({ status }: { status: string }) {
    const life = lifecycleOf(status);
    return (
        <span
            className={cn(
                "inline-flex w-fit items-center rounded-sm border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
                tones[life],
            )}
        >
            {lifecycleLabel[life]}
        </span>
    );
}
