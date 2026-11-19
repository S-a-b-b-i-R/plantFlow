import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Field } from "@/components/orders/Field";
import { inputClass, panelClass } from "@/components/orders/styles";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import { money } from "@/lib/orders";
import {
    rate,
    scopeLabel,
    unitLabel,
    type FloorBreakdown,
    type MarginRow,
} from "@/lib/pricing";
import { cn } from "@/lib/utils";
import { CustomerPicker } from "./CustomerPicker";
import { usePlants } from "./useBook";

const grid = "md:grid-cols-[1fr_6rem_7rem_10rem_9rem_8rem]";

type Scenario = {
    plantId: string;
    date: string;
    distance: string;
    customerId: string;
};

const query = (s: Scenario) =>
    new URLSearchParams({
        plantId: s.plantId,
        date: s.date,
        distanceKm: s.distance.trim() || "0",
        ...(s.customerId ? { customerId: s.customerId } : {}),
    });

// For every active product: what it costs, the lowest price allowed, the price in force and what is left.
export function MarginReport() {
    const plants = usePlants();
    const [plantId, setPlantId] = useState("");
    const [date, setDate] = useState(() =>
        new Date().toLocaleDateString("en-CA"),
    );
    const [distance, setDistance] = useState("0");
    const [customerId, setCustomerId] = useState("");
    const [customerLabel, setCustomerLabel] = useState("");

    // Until a plant is picked, the first one is used.
    const plant = plantId || plants.data?.items[0]?.id || "";
    const scenario: Scenario = { plantId: plant, date, distance, customerId };
    const km = Number(distance.trim() || "0");
    const ready = plant !== "" && date !== "" && Number.isFinite(km) && km >= 0;

    const report = useQuery({
        queryKey: ["pricebook", "margins", query(scenario).toString()],
        queryFn: () => api<MarginRow[]>(`/api/cost-margin?${query(scenario)}`),
        enabled: ready,
        placeholderData: (previous) => previous,
    });

    const rows = report.data ?? [];
    const below = rows.filter((r) => r.belowFloor).length;
    const missing = rows.filter((r) => r.totalCost === null).length;

    return (
        <div className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <Field label="Plant">
                    <select
                        value={plant}
                        onChange={(e) => setPlantId(e.target.value)}
                        className={inputClass}
                    >
                        {plants.data?.items.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.code} {p.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field
                    label="On"
                    hint="Costs and prices in force on this date."
                >
                    <input
                        type="date"
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                        className={inputClass}
                    />
                </Field>
                <Field label="Distance (km)" hint="Sets the delivery charge.">
                    <input
                        inputMode="decimal"
                        value={distance}
                        onChange={(e) => setDistance(e.target.value)}
                        className={inputClass}
                    />
                </Field>
                <Field
                    label="Customer"
                    hint="Adds their AIT and their own prices."
                >
                    <CustomerPicker
                        label={customerLabel}
                        placeholder="Everyone. Type 2 letters"
                        onPick={(c) => {
                            setCustomerId(c?.id ?? "");
                            setCustomerLabel(c?.code ?? "");
                        }}
                    />
                </Field>
            </div>

            {!ready ? (
                <p className="text-sm text-muted-foreground">
                    Choose a plant and a date, and a distance of 0 or more.
                </p>
            ) : report.isPending ? (
                <Skeleton className="h-64 w-full" />
            ) : report.isError ? (
                <p className="text-sm font-medium text-late" role="alert">
                    The report could not be loaded. {report.error.message}
                </p>
            ) : rows.length === 0 ? (
                <p
                    className={cn(
                        panelClass,
                        "px-4 py-12 text-center text-sm text-muted-foreground",
                    )}
                >
                    There are no active products yet.
                </p>
            ) : (
                <>
                    <p className="text-sm text-muted-foreground">
                        {rows.length} products.{" "}
                        <span
                            className={
                                below > 0 ? "font-semibold text-late" : ""
                            }
                        >
                            {below} priced below the floor.
                        </span>{" "}
                        {missing > 0 &&
                            `${missing} cannot be worked out yet. Their notes say what is missing.`}
                    </p>
                    <section className={cn(panelClass, "overflow-hidden")}>
                        <div
                            className={cn(
                                "hidden gap-3 border-b bg-muted/50 px-4 py-2 text-xs font-medium text-muted-foreground md:grid",
                                grid,
                            )}
                        >
                            <span>Product</span>
                            <span className="text-right">Cost</span>
                            <span className="text-right">Floor price</span>
                            <span className="text-right">Price in force</span>
                            <span className="text-right">Margin</span>
                            <span>Check</span>
                        </div>
                        <ul className="divide-y">
                            {rows.map((row) => (
                                <MarginLine
                                    key={row.productId}
                                    row={row}
                                    scenario={scenario}
                                />
                            ))}
                        </ul>
                    </section>
                </>
            )}
        </div>
    );
}

function MarginLine({ row, scenario }: { row: MarginRow; scenario: Scenario }) {
    const [open, setOpen] = useState(false);
    const worked = row.totalCost !== null;

    return (
        <li className="space-y-2 px-4 py-3">
            <div className={cn("grid gap-x-3 gap-y-1 md:items-center", grid)}>
                <span className="min-w-0">
                    <span className="block truncate font-semibold">
                        {row.productCode}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                        {row.productName}
                    </span>
                </span>
                <span className="text-sm tabular-nums md:text-right">
                    {row.totalCost !== null ? money(row.totalCost) : "–"}
                </span>
                <span className="text-sm font-medium tabular-nums md:text-right">
                    {row.floorPrice !== null ? money(row.floorPrice) : "–"}
                </span>
                <span className="md:text-right">
                    <span className="block text-sm tabular-nums">
                        {row.activePrice !== null
                            ? money(row.activePrice)
                            : "–"}
                    </span>
                    {row.priceScope && (
                        <span className="block text-xs text-muted-foreground">
                            {scopeLabel[row.priceScope]}
                        </span>
                    )}
                </span>
                <span
                    className={cn(
                        "text-sm tabular-nums md:text-right",
                        row.marginAmount !== null &&
                            row.marginAmount < 0 &&
                            "font-medium text-late",
                    )}
                >
                    {row.marginAmount !== null && row.marginPercent !== null
                        ? `${money(row.marginAmount)} (${row.marginPercent}%)`
                        : "–"}
                </span>
                <span
                    className={cn(
                        "inline-flex w-fit rounded-sm border px-2 py-0.5 text-xs font-semibold",
                        row.belowFloor === null
                            ? "border-border bg-muted text-muted-foreground"
                            : row.belowFloor
                              ? "border-late/40 bg-late/10 text-late"
                              : "border-ok/40 bg-ok/10 text-ok",
                    )}
                >
                    {row.belowFloor === null
                        ? "Not checked"
                        : row.belowFloor
                          ? "Below floor"
                          : "Above floor"}
                </span>
            </div>
            {row.note && (
                <p className="text-sm text-muted-foreground">{row.note}</p>
            )}
            {worked && (
                <Button
                    variant="ghost"
                    className="h-9 px-3"
                    onClick={() => setOpen(!open)}
                >
                    {open ? "Hide the working" : "Show the working"}
                </Button>
            )}
            {open && <Working productId={row.productId} scenario={scenario} />}
        </li>
    );
}

// The floor price worked out line by line, so a number can be explained to a customer or a manager.
function Working({
    productId,
    scenario,
}: {
    productId: string;
    scenario: Scenario;
}) {
    const working = useQuery({
        queryKey: [
            "pricebook",
            "working",
            productId,
            query(scenario).toString(),
        ],
        queryFn: () =>
            api<FloorBreakdown>(
                `/api/floor-price?productId=${productId}&${query(scenario)}`,
            ),
    });

    if (working.isPending) return <Skeleton className="h-32 w-full" />;
    if (working.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                {working.error.message}
            </p>
        );
    }

    const w = working.data;
    const extra: [string, number][] = [
        ["Overhead", w.overhead],
        ["Fuel", w.fuel],
        ["Bridge toll", w.bridgeToll],
        ["Delivery", w.delivery],
    ];

    return (
        <dl className="max-w-xl divide-y rounded-sm border bg-muted/30 text-sm">
            <p className="px-3 py-1.5 text-xs text-muted-foreground">
                Per CFT, recipe version {w.mixDesignVersion}
            </p>
            {w.materials.map((m) => (
                <Line
                    key={m.materialCode}
                    label={`${m.materialCode}: ${rate(m.quantityPerCft)} ${unitLabel[m.unit]} at ${rate(m.costPerUnit)}`}
                    value={money(m.cost)}
                />
            ))}
            <Line label="Materials" value={money(w.materialCost)} strong />
            {extra
                .filter(([, amount]) => amount > 0)
                .map(([label, amount]) => (
                    <Line key={label} label={label} value={money(amount)} />
                ))}
            <Line label="Total cost" value={money(w.totalCost)} strong />
            <Line
                label={`Minimum margin ${w.marginPercent}% and AIT ${w.aitPercent}% of the price`}
                value=""
            />
            <Line label="Floor price" value={money(w.floorPrice)} strong />
            <Line
                label={`With VAT ${w.vatPercent}% (${money(w.vat)})`}
                value={money(w.floorPriceWithVat)}
            />
        </dl>
    );
}

function Line({
    label,
    value,
    strong,
}: {
    label: string;
    value: string;
    strong?: boolean;
}) {
    return (
        <div
            className={cn(
                "flex items-center justify-between gap-3 px-3 py-1.5",
                strong && "font-semibold",
            )}
        >
            <dt>{label}</dt>
            <dd className="tabular-nums">{value}</dd>
        </div>
    );
}
