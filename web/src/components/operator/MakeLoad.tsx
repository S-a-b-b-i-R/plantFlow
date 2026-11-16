import { useState } from "react";
import { LuPlus, LuSearch, LuTruck, LuX } from "react-icons/lu";
import { Button } from "@/components/ui/button";
import { cft } from "@/lib/format";
import type { TruckOption } from "@/lib/types";
import { useAct } from "./useAct";

type Mode = "usual" | "other" | "over";

const inputClass =
    "h-12 w-full rounded-md border bg-card px-3 text-base placeholder:text-muted-foreground";

// Make a load: choose how much, then tap the truck. One tap on a truck makes the load.
export function MakeLoad({
    pourId,
    remaining,
    trucks,
    canOverride,
}: {
    pourId: string;
    remaining: number;
    trucks: TruckOption[];
    canOverride: boolean;
}) {
    const act = useAct();
    const [open, setOpen] = useState(false);
    const [mode, setMode] = useState<Mode>("usual");
    const [query, setQuery] = useState("");
    const [amount, setAmount] = useState("");
    const [reason, setReason] = useState("");

    const fullyMade = remaining <= 0;
    const effective: Mode = fullyMade && mode === "usual" ? "over" : mode;

    const close = () => {
        setOpen(false);
        setMode("usual");
        setQuery("");
        setAmount("");
        setReason("");
        act.reset();
    };

    if (!open) {
        return (
            <div className="flex flex-wrap items-center gap-3">
                <Button
                    className="h-12 gap-2 bg-signal px-5 text-base font-semibold text-ink hover:bg-signal/90"
                    disabled={fullyMade && !canOverride}
                    onClick={() => {
                        setMode(fullyMade ? "over" : "usual");
                        setOpen(true);
                    }}
                >
                    <LuPlus className="size-5" aria-hidden="true" />
                    {fullyMade ? "Make a load over plan" : "Make a load"}
                </Button>
                {fullyMade && !canOverride && (
                    <p className="text-sm text-muted-foreground">
                        Fully produced. Going over plan needs a plant manager.
                    </p>
                )}
            </div>
        );
    }

    const needle = query.trim().toLowerCase();
    const shown = trucks
        .filter(
            (t) =>
                !needle ||
                t.code.toLowerCase().includes(needle) ||
                (t.plateNumber ?? "").toLowerCase().includes(needle),
        )
        .sort((a, b) => Number(a.busy) - Number(b.busy));

    const quantity = Number(amount);
    const blocked =
        (effective === "other" && !(quantity > 0)) ||
        (effective === "over" && reason.trim().length < 3);

    const sizeFor = (t: TruckOption) =>
        effective === "other" && quantity > 0
            ? quantity
            : effective === "over"
              ? quantity > 0
                  ? quantity
                  : t.capacityCft
              : Math.min(t.capacityCft, remaining);

    const make = (truck: TruckOption) => {
        const body: Record<string, unknown> = { truckId: truck.id };
        if (effective === "other") {
            body.quantityCft = quantity;
            if (reason.trim()) body.reason = reason.trim();
        }
        if (effective === "over") {
            body.overrideReason = reason.trim();
            if (quantity > 0) body.quantityCft = quantity;
        }
        act.mutate(
            {
                url:
                    effective === "over"
                        ? `/api/pours/${pourId}/loads/override`
                        : `/api/pours/${pourId}/loads`,
                body,
            },
            { onSuccess: close },
        );
    };

    const modes: [Mode, string][] = [
        ["usual", "Usual load"],
        ["other", "Other amount"],
        ...(canOverride ? ([["over", "Over plan"]] as [Mode, string][]) : []),
    ];

    return (
        <div className="rounded-sm border bg-muted/40 p-4">
            <div className="flex items-start justify-between gap-3">
                <div
                    className="flex flex-wrap gap-2"
                    role="group"
                    aria-label="Load size"
                >
                    {modes.map(([value, label]) => (
                        <button
                            key={value}
                            type="button"
                            disabled={fullyMade && value !== "over"}
                            aria-pressed={effective === value}
                            onClick={() => setMode(value)}
                            className={`h-11 rounded-md border px-4 text-sm font-medium disabled:opacity-40 ${
                                effective === value
                                    ? "border-ink bg-ink text-white"
                                    : "bg-card"
                            }`}
                        >
                            {label}
                        </button>
                    ))}
                </div>
                <Button
                    variant="ghost"
                    size="icon"
                    className="size-11"
                    aria-label="Close"
                    onClick={close}
                >
                    <LuX className="size-5" aria-hidden="true" />
                </Button>
            </div>

            {effective !== "usual" && (
                <div className="mt-3 grid gap-3 sm:grid-cols-[10rem_1fr]">
                    <label className="text-sm">
                        <span className="mb-1 block text-muted-foreground">
                            {effective === "over"
                                ? "Amount (optional)"
                                : "Amount in CFT"}
                        </span>
                        <input
                            inputMode="decimal"
                            value={amount}
                            onChange={(e) => setAmount(e.target.value)}
                            className={inputClass}
                        />
                    </label>
                    <label className="text-sm">
                        <span className="mb-1 block text-muted-foreground">
                            {effective === "over"
                                ? "Why go over plan"
                                : "Why smaller than usual (if it is)"}
                        </span>
                        <input
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            className={inputClass}
                        />
                    </label>
                </div>
            )}

            <div className="relative mt-3">
                <LuSearch
                    className="pointer-events-none absolute left-3 top-1/2 size-5 -translate-y-1/2 text-muted-foreground"
                    aria-hidden="true"
                />
                <input
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    placeholder="Find a truck by code or plate"
                    className={`${inputClass} pl-10`}
                    aria-label="Find a truck"
                />
            </div>

            <p className="mt-3 text-sm text-muted-foreground">
                {blocked
                    ? effective === "other"
                        ? "Enter the amount, then tap a truck."
                        : "Give a reason, then tap a truck."
                    : "Tap a truck to make the load."}
            </p>

            <ul className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                {shown.map((truck) => (
                    <li key={truck.id}>
                        <button
                            type="button"
                            disabled={truck.busy || blocked || act.isPending}
                            onClick={() => make(truck)}
                            className="flex min-h-14 w-full items-center gap-3 rounded-md border bg-card px-3 py-2 text-left hover:border-ink disabled:cursor-not-allowed disabled:opacity-45"
                        >
                            <LuTruck
                                className="size-5 shrink-0"
                                aria-hidden="true"
                            />
                            <span className="min-w-0 flex-1">
                                <span className="block font-semibold">
                                    {truck.code}
                                </span>
                                <span className="block truncate text-xs text-muted-foreground">
                                    {truck.plateNumber ?? "No plate"}
                                </span>
                            </span>
                            <span className="text-right text-sm tabular-nums">
                                {truck.busy ? (
                                    <span className="text-muted-foreground">
                                        Still out
                                    </span>
                                ) : (
                                    <>
                                        <span className="font-semibold">
                                            {cft(sizeFor(truck))}
                                        </span>{" "}
                                        <span className="text-muted-foreground">
                                            CFT
                                        </span>
                                    </>
                                )}
                            </span>
                        </button>
                    </li>
                ))}
                {shown.length === 0 && (
                    <li className="text-sm text-muted-foreground">
                        No truck matches.
                    </li>
                )}
            </ul>

            {act.isError && (
                <p className="mt-3 text-sm font-medium text-late" role="alert">
                    {act.error.message}
                </p>
            )}
        </div>
    );
}
