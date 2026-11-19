import { useQuery } from "@tanstack/react-query";
import { LuPlus, LuTrash2 } from "react-icons/lu";
import { Field } from "./Field";
import { inputClass } from "./styles";
import {
    validPrice,
    validQuantity,
    type Draft,
    type LineDraft,
} from "./orderDraft";
import { api } from "@/lib/api";
import {
    elementLabel,
    methodLabel,
    money,
    type PourElement,
    type PourMethod,
    type PriceMatch,
    type ProductOption,
} from "@/lib/orders";

export function ConcreteStep({
    draft,
    products,
    updateLine,
    addLine,
    removeLine,
}: {
    draft: Draft;
    products: ProductOption[];
    updateLine: (key: string, patch: Partial<LineDraft>) => void;
    addLine: () => void;
    removeLine: (key: string) => void;
}) {
    return (
        <div className="space-y-4">
            <div>
                <h2 className="display text-2xl">What concrete?</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                    One line for each mix and pour. Leave the price empty to use
                    the list price for this plant and date.
                </p>
            </div>

            {draft.lines.map((line, index) => (
                <LineEditor
                    key={line.key}
                    index={index}
                    line={line}
                    draft={draft}
                    products={products}
                    canRemove={draft.lines.length > 1}
                    update={(patch) => updateLine(line.key, patch)}
                    remove={() => removeLine(line.key)}
                />
            ))}

            <button
                type="button"
                onClick={addLine}
                className="inline-flex h-10 items-center gap-2 rounded-sm border border-dashed border-ink/40 bg-card px-4 text-sm font-medium hover:border-ink"
            >
                <LuPlus aria-hidden /> Add another line
            </button>
        </div>
    );
}

function LineEditor({
    index,
    line,
    draft,
    products,
    canRemove,
    update,
    remove,
}: {
    index: number;
    line: LineDraft;
    draft: Draft;
    products: ProductOption[];
    canRemove: boolean;
    update: (patch: Partial<LineDraft>) => void;
    remove: () => void;
}) {
    const product = products.find((p) => p.id === line.productId);

    const list = useQuery({
        queryKey: [
            "price",
            line.productId,
            draft.plantId,
            draft.customerId,
            draft.date,
        ],
        queryFn: () =>
            api<PriceMatch>(
                `/api/prices/lookup?productId=${line.productId}&plantId=${draft.plantId}&customerId=${draft.customerId}&date=${draft.date}`,
            ),
        enabled: !!line.productId && !!draft.plantId && !!draft.date,
        retry: false,
    });

    const quantityBad = line.quantity !== "" && !validQuantity(line.quantity);
    const priceBad = !validPrice(line.unitPrice);
    // The lookup answers 404 when no price applies.
    const noPrice = list.isError && line.unitPrice.trim() === "";

    return (
        <fieldset className="rounded-sm border bg-card p-4">
            <legend className="px-1 text-sm font-semibold">
                Line {index + 1}
            </legend>
            <div className="grid gap-3 sm:grid-cols-6">
                <Field label="Mix" className="sm:col-span-3">
                    <select
                        value={line.productId}
                        onChange={(e) => update({ productId: e.target.value })}
                        className={inputClass}
                    >
                        <option value="">Choose a mix</option>
                        {products.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.code}, {p.name}
                                {p.strengthClass && p.strengthClass !== p.code
                                    ? `, ${p.strengthClass}`
                                    : ""}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field
                    label="Quantity, CFT"
                    hint={
                        quantityBad
                            ? "Use a number above 0 with at most 2 decimals."
                            : undefined
                    }
                    className="sm:col-span-1"
                >
                    <input
                        inputMode="decimal"
                        value={line.quantity}
                        onChange={(e) => update({ quantity: e.target.value })}
                        aria-invalid={quantityBad}
                        className={inputClass}
                    />
                </Field>
                <Field
                    label="Price per CFT"
                    hint={
                        priceBad
                            ? "Use a number above 0 with at most 2 decimals."
                            : noPrice
                              ? "No active price. Enter one."
                              : list.data
                                ? `List price ${money(list.data.pricePerCft)}`
                                : undefined
                    }
                    className="sm:col-span-2"
                >
                    <input
                        inputMode="decimal"
                        value={line.unitPrice}
                        onChange={(e) => update({ unitPrice: e.target.value })}
                        placeholder={
                            list.data ? money(list.data.pricePerCft) : ""
                        }
                        aria-invalid={priceBad || noPrice}
                        className={inputClass}
                    />
                </Field>
                <Field label="Poured as" className="sm:col-span-2">
                    <select
                        value={line.element}
                        onChange={(e) =>
                            update({ element: e.target.value as PourElement })
                        }
                        className={inputClass}
                    >
                        {(Object.keys(elementLabel) as PourElement[]).map(
                            (k) => (
                                <option key={k} value={k}>
                                    {elementLabel[k]}
                                </option>
                            ),
                        )}
                    </select>
                </Field>
                <Field label="Placed by" className="sm:col-span-2">
                    <select
                        value={line.method}
                        onChange={(e) =>
                            update({ method: e.target.value as PourMethod })
                        }
                        className={inputClass}
                    >
                        {(Object.keys(methodLabel) as PourMethod[]).map((k) => (
                            <option key={k} value={k}>
                                {methodLabel[k]}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="Slump, mm" className="sm:col-span-2">
                    <input
                        inputMode="numeric"
                        value={line.slump}
                        onChange={(e) => update({ slump: e.target.value })}
                        placeholder={
                            product ? String(product.defaultSlumpMm) : ""
                        }
                        className={inputClass}
                    />
                </Field>
                <Field label="Notes" className="sm:col-span-6">
                    <input
                        value={line.notes}
                        onChange={(e) => update({ notes: e.target.value })}
                        maxLength={500}
                        className={inputClass}
                    />
                </Field>
            </div>
            {canRemove && (
                <button
                    type="button"
                    onClick={remove}
                    className="mt-3 inline-flex h-9 items-center gap-2 rounded-sm px-2 text-sm text-late hover:bg-late/10"
                >
                    <LuTrash2 aria-hidden /> Remove this line
                </button>
            )}
        </fieldset>
    );
}
