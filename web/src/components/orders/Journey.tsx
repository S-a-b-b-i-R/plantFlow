import type { OrderStatus } from "@/lib/orders";
import { cn } from "@/lib/utils";

const stages = [
    "Draft",
    "In approval",
    "Approved",
    "Confirmed",
    "Delivering",
    "Completed",
];

const position: Partial<Record<OrderStatus, number>> = {
    Draft: 0,
    Submitted: 1,
    InApproval: 1,
    Approved: 2,
    Confirmed: 3,
    InProgress: 4,
    Completed: 5,
};

// Where the order is on its way. Held, cancelled and rejected orders are off the road, so they show none.
export function Journey({ status }: { status: OrderStatus }) {
    const at = position[status];
    if (at === undefined) return null;

    return (
        <ol
            className="grid grid-cols-3 gap-1 sm:grid-cols-6"
            aria-label="Progress"
        >
            {stages.map((label, i) => (
                <li
                    key={label}
                    aria-current={i === at ? "step" : undefined}
                    className={cn(
                        "border-t-4 pt-1.5 text-xs",
                        i < at && "border-ink text-foreground",
                        i === at &&
                            "border-signal font-semibold text-foreground",
                        i > at && "border-border text-muted-foreground",
                    )}
                >
                    {label}
                </li>
            ))}
        </ol>
    );
}
