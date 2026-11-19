import { Field } from "./Field";
import { inputClass, panelClass } from "./styles";
import { parseAmount, type Draft } from "./orderDraft";
import { useStoredDistance } from "./useStoredDistance";
import { cft } from "@/lib/format";
import {
    elementLabel,
    methodLabel,
    money,
    shortDate,
    type PlantOption,
    type ProductOption,
} from "@/lib/orders";

export function ReviewStep({
    draft,
    products,
    siteName,
    plant,
    reason,
    setReason,
    needsReason,
    explain,
}: {
    draft: Draft;
    products: ProductOption[];
    siteName: string;
    plant: PlantOption | undefined;
    reason: string;
    setReason: (value: string) => void;
    needsReason: boolean;
    explain: string;
}) {
    const stored = useStoredDistance(draft.siteId, draft.plantId);
    const km = parseAmount(draft.distance) ?? stored.km;
    const total = draft.lines.reduce(
        (sum, l) =>
            sum + Number(l.quantity || 0) * (parseAmount(l.unitPrice) ?? 0),
        0,
    );
    const quantity = draft.lines.reduce(
        (sum, l) => sum + Number(l.quantity || 0),
        0,
    );
    const anyListPrice = draft.lines.some(
        (l) => parseAmount(l.unitPrice) === null,
    );

    return (
        <div className="space-y-6">
            <div>
                <h2 className="display text-2xl">Check it over</h2>
                <p className="mt-1 text-sm text-muted-foreground">{explain}</p>
            </div>

            <dl
                className={`${panelClass} grid gap-x-6 gap-y-3 p-4 text-sm sm:grid-cols-2`}
            >
                <Item label="Customer" value={draft.customerName} />
                <Item label="Site" value={siteName} />
                <Item
                    label="Plant"
                    value={plant ? `${plant.name} (${plant.code})` : ""}
                />
                <Item
                    label="Needed on"
                    value={draft.date ? shortDate(draft.date) : ""}
                />
                <Item
                    label="Distance"
                    value={km !== null ? `${km} km` : "Not set"}
                />
                <Item
                    label="Payment terms"
                    value={draft.paymentTerms || "Not set"}
                />
                {draft.notes && <Item label="Notes" value={draft.notes} />}
            </dl>

            <section className={`${panelClass} overflow-x-auto`}>
                <table className="w-full min-w-136 text-sm">
                    <thead className="border-b bg-muted/50 text-left text-xs font-medium text-muted-foreground">
                        <tr>
                            <th className="px-4 py-2">Mix</th>
                            <th className="px-4 py-2">Pour</th>
                            <th className="px-4 py-2 text-right">CFT</th>
                            <th className="px-4 py-2 text-right">Price</th>
                            <th className="px-4 py-2 text-right">Value</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y">
                        {draft.lines.map((l) => {
                            const product = products.find(
                                (p) => p.id === l.productId,
                            );
                            const price = parseAmount(l.unitPrice);
                            return (
                                <tr key={l.key}>
                                    <td className="px-4 py-2 font-medium">
                                        {product?.code ?? ""}
                                    </td>
                                    <td className="px-4 py-2 text-muted-foreground">
                                        {elementLabel[l.element]},{" "}
                                        {methodLabel[l.method]}
                                    </td>
                                    <td className="px-4 py-2 text-right tabular-nums">
                                        {cft(Number(l.quantity))}
                                    </td>
                                    <td className="px-4 py-2 text-right tabular-nums">
                                        {price !== null
                                            ? money(price)
                                            : "List price"}
                                    </td>
                                    <td className="px-4 py-2 text-right tabular-nums">
                                        {price !== null
                                            ? money(Number(l.quantity) * price)
                                            : "Set on save"}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                    <tfoot className="border-t font-semibold">
                        <tr>
                            <td className="px-4 py-2" colSpan={2}>
                                Total
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {cft(quantity)}
                            </td>
                            <td />
                            <td className="px-4 py-2 text-right tabular-nums">
                                {anyListPrice ? "At least " : ""}
                                {money(total)}
                            </td>
                        </tr>
                    </tfoot>
                </table>
            </section>

            <p className="text-sm text-muted-foreground">
                The floor price is checked when the order is submitted. A line
                priced under it needs Management as well.
            </p>

            {needsReason && (
                <Field
                    label="Why is it being changed?"
                    hint="This goes in the order's history."
                >
                    <input
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        maxLength={500}
                        className={inputClass}
                    />
                </Field>
            )}
        </div>
    );
}

function Item({ label, value }: { label: string; value: string }) {
    return (
        <div>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="font-medium">{value}</dd>
        </div>
    );
}
