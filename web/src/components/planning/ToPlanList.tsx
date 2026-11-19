import { LuTriangleAlert } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import { elementLabel, methodLabel, shortDate } from "@/lib/orders";
import type { LineToPlan } from "@/lib/planning";
import { cn } from "@/lib/utils";
import { useToPlan } from "./usePlanning";

// Lines of confirmed or running orders, requested for this day or earlier, with CFT still to put on a shift.
export function ToPlanList({
    plantId,
    date,
    canPlan,
    activeLineId,
    onPlan,
}: {
    plantId: string;
    date: string;
    canPlan: boolean;
    activeLineId: string | undefined;
    onPlan: (line: LineToPlan) => void;
}) {
    const lines = useToPlan(plantId, date);

    return (
        <aside aria-label="Orders to plan" className="space-y-3">
            <div>
                <h2 className="display text-2xl leading-none">
                    Orders to plan
                </h2>
                <p className="mt-1 text-sm text-muted-foreground">
                    Confirmed orders for this day, and older ones that still
                    have concrete left to plan.
                </p>
            </div>

            {lines.isLoading && <Skeleton className="h-32 w-full" />}
            {lines.isError && (
                <p role="alert" className="text-sm text-late">
                    The orders could not be loaded.
                </p>
            )}
            {lines.data && lines.data.length === 0 && (
                <p
                    className={cn(
                        panelClass,
                        "p-4 text-sm text-muted-foreground",
                    )}
                >
                    Everything requested up to this day is planned.
                </p>
            )}

            <ul className="space-y-2">
                {lines.data?.map((line) => (
                    <li
                        key={line.orderLineId}
                        className={cn(
                            panelClass,
                            "p-3",
                            line.overdue && "border-late/50",
                            activeLineId === line.orderLineId &&
                                "border-ink ring-1 ring-ink",
                        )}
                    >
                        <p className="text-sm font-semibold">
                            {line.orderNumber} · {line.customerName}
                        </p>
                        <p className="text-sm text-muted-foreground">
                            {line.siteName}
                        </p>
                        <p className="mt-1 text-sm">
                            {line.productCode} ·{" "}
                            {elementLabel[line.pourElement]} ·{" "}
                            {methodLabel[line.pourMethod]}
                        </p>
                        <p className="text-sm">
                            <span className="font-semibold">
                                {cft(line.remainingCft)} CFT
                            </span>{" "}
                            <span className="text-muted-foreground">
                                left of {cft(line.quantityCft)}
                            </span>
                        </p>
                        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                            {line.overdue ? (
                                <span className="inline-flex items-center gap-1 text-xs font-semibold text-late">
                                    <LuTriangleAlert
                                        aria-hidden
                                        className="size-3.5"
                                    />
                                    Overdue · wanted{" "}
                                    {shortDate(line.requestedDate)}
                                </span>
                            ) : (
                                <span className="text-xs text-muted-foreground">
                                    Wanted {shortDate(line.requestedDate)}
                                </span>
                            )}
                            {canPlan && (
                                <Button
                                    variant="outline"
                                    className="h-9 px-3"
                                    onClick={() => onPlan(line)}
                                >
                                    Plan pour
                                </Button>
                            )}
                        </div>
                    </li>
                ))}
            </ul>
        </aside>
    );
}
