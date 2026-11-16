import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { OperatorLoad } from "@/lib/types";
import { useAct } from "./useAct";

export type IssueKind = "cancel" | "rejectPlant" | "rejectSite" | "part";

const titles: Record<IssueKind, string> = {
    cancel: "Cancel this load",
    rejectPlant: "Reject this load at the plant",
    rejectSite: "Reject this load at the site",
    part: "Concrete came back after pouring",
};

const submitLabels: Record<IssueKind, string> = {
    cancel: "Cancel load",
    rejectPlant: "Reject load",
    rejectSite: "Reject load",
    part: "Record the return",
};

const inputClass =
    "h-12 w-full rounded-md border bg-card px-3 text-base placeholder:text-muted-foreground";

type Fault = "Company" | "Customer";

export function LoadIssue({
    load,
    kind,
    onClose,
}: {
    load: OperatorLoad;
    kind: IssueKind;
    onClose: () => void;
}) {
    const act = useAct();
    const [reason, setReason] = useState("");
    const [fault, setFault] = useState<Fault | "">(
        kind === "rejectPlant" ? "Company" : "",
    );
    const [amount, setAmount] = useState("");

    const needsFault = kind === "rejectSite" || kind === "part";
    const ready =
        reason.trim().length >= 3 &&
        (!needsFault || fault !== "") &&
        (kind !== "part" || Number(amount) > 0);

    const submit = () => {
        const base = { reason: reason.trim() };
        const request =
            kind === "cancel"
                ? { url: `/api/loads/${load.id}/cancel`, body: base }
                : kind === "part"
                  ? {
                        url: `/api/loads/${load.id}/part-return`,
                        body: { ...base, fault, quantityCft: Number(amount) },
                    }
                  : {
                        url: `/api/loads/${load.id}/reject`,
                        body: needsFault ? { ...base, fault } : base,
                    };
        act.mutate(request, { onSuccess: onClose });
    };

    return (
        <div className="basis-full rounded-sm border bg-muted/40 p-4">
            <p className="font-semibold">{titles[kind]}</p>

            {kind === "part" && (
                <label className="mt-3 block text-sm">
                    <span className="mb-1 block text-muted-foreground">
                        How much came back, in CFT (less than {load.quantityCft}
                        )
                    </span>
                    <input
                        inputMode="decimal"
                        value={amount}
                        onChange={(e) => setAmount(e.target.value)}
                        className={`${inputClass} sm:w-48`}
                    />
                </label>
            )}

            {needsFault && (
                <div
                    className="mt-3 flex flex-wrap gap-2"
                    role="group"
                    aria-label="Whose fault"
                >
                    {(["Company", "Customer"] as const).map((value) => (
                        <button
                            key={value}
                            type="button"
                            aria-pressed={fault === value}
                            onClick={() => setFault(value)}
                            className={`h-11 rounded-md border px-4 text-sm font-medium ${
                                fault === value
                                    ? "border-ink bg-ink text-white"
                                    : "bg-card"
                            }`}
                        >
                            {value === "Company"
                                ? "Company's fault"
                                : "Customer's fault"}
                        </button>
                    ))}
                </div>
            )}

            <label className="mt-3 block text-sm">
                <span className="mb-1 block text-muted-foreground">Reason</span>
                <input
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    className={inputClass}
                />
            </label>

            {act.isError && (
                <p className="mt-3 text-sm font-medium text-late" role="alert">
                    {act.error.message}
                </p>
            )}

            <div className="mt-4 flex flex-wrap gap-2">
                <Button
                    className="h-12 px-5 text-base"
                    disabled={!ready || act.isPending}
                    onClick={submit}
                >
                    {submitLabels[kind]}
                </Button>
                <Button
                    variant="outline"
                    className="h-12 px-5 text-base"
                    onClick={onClose}
                >
                    Never mind
                </Button>
            </div>
        </div>
    );
}
