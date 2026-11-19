import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import {
    customerTypes,
    validPercent,
    type CustomerType,
    type PricingPolicy,
} from "@/lib/pricing";
import { cn } from "@/lib/utils";

// VAT, the minimum margin and the AIT each customer type deducts. These feed every floor price.
export function PolicyPanel() {
    const policy = useQuery({
        queryKey: ["pricebook", "policy"],
        queryFn: () => api<PricingPolicy>("/api/pricing-policy"),
    });

    if (policy.isPending) return <Skeleton className="h-64 w-full" />;
    if (policy.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                The pricing rules could not be loaded. {policy.error.message}
            </p>
        );
    }

    return <PolicyForm saved={policy.data} />;
}

function PolicyForm({ saved }: { saved: PricingPolicy }) {
    const queryClient = useQueryClient();
    const [vat, setVat] = useState(String(saved.vatPercent));
    const [margin, setMargin] = useState(String(saved.minMarginPercent));
    // A customer type with no AIT rate is an empty box.
    const [ait, setAit] = useState<Record<CustomerType, string>>(() => {
        const start = { Contractor: "", Developer: "", Individual: "" };
        for (const rate of saved.aitRates) {
            start[rate.customerType] = String(rate.percent);
        }
        return start;
    });

    const save = useMutation({
        mutationFn: (body: PricingPolicy) =>
            api<unknown>("/api/pricing-policy", {
                method: "PUT",
                body: JSON.stringify(body),
            }),
        onSuccess: () =>
            queryClient.invalidateQueries({ queryKey: ["pricebook"] }),
    });

    const aitFilled = customerTypes.filter((t) => ait[t].trim() !== "");
    const valid =
        validPercent(vat, 100) &&
        validPercent(margin, 60) &&
        aitFilled.every((t) => validPercent(ait[t], 30));

    const body: PricingPolicy = {
        vatPercent: Number(vat),
        minMarginPercent: Number(margin),
        aitRates: aitFilled.map((t) => ({
            customerType: t,
            percent: Number(ait[t]),
        })),
    };
    const unchanged = JSON.stringify(body) === JSON.stringify(saved);

    return (
        <form
            onSubmit={(event) => {
                event.preventDefault();
                if (valid && !unchanged && !save.isPending) save.mutate(body);
            }}
            className={cn(panelClass, "max-w-3xl space-y-5 p-4")}
        >
            <p className="text-sm text-muted-foreground">
                These rules apply to every floor price from the moment they are
                saved. They are not sent for approval, so change them with care.
            </p>

            <div className="grid gap-3 sm:grid-cols-2">
                <Field
                    label="VAT (%)"
                    hint="Added on top of the price and left out of the floor price."
                >
                    <input
                        inputMode="decimal"
                        value={vat}
                        onChange={(e) => setVat(e.target.value)}
                        className={inputClass}
                    />
                </Field>
                <Field
                    label="Minimum margin (%)"
                    hint="Profit as a share of the selling price. Up to 60."
                >
                    <input
                        inputMode="decimal"
                        value={margin}
                        onChange={(e) => setMargin(e.target.value)}
                        className={inputClass}
                    />
                </Field>
            </div>

            <fieldset className="space-y-2">
                <legend className="text-sm font-medium">
                    AIT the customer deducts from payment (%)
                </legend>
                <p className="text-xs text-muted-foreground">
                    Counted as a cost in the floor price. Leave a type empty if
                    it pays no AIT. Up to 30.
                </p>
                <div className="grid gap-3 sm:grid-cols-3">
                    {customerTypes.map((type) => (
                        <Field key={type} label={type}>
                            <input
                                inputMode="decimal"
                                value={ait[type]}
                                placeholder="None"
                                onChange={(e) =>
                                    setAit({ ...ait, [type]: e.target.value })
                                }
                                className={inputClass}
                            />
                        </Field>
                    ))}
                </div>
            </fieldset>

            {save.isError && (
                <p className="text-sm font-medium text-late" role="alert">
                    {save.error.message}
                </p>
            )}
            {save.isSuccess && unchanged && (
                <p className="text-sm font-medium text-ok" role="status">
                    Saved. Price checks use these rules now.
                </p>
            )}

            <Button
                type="submit"
                className={cn("h-10 px-5", nextButtonClass)}
                disabled={!valid || unchanged || save.isPending}
            >
                {save.isPending ? "Saving…" : "Save pricing rules"}
            </Button>
        </form>
    );
}
