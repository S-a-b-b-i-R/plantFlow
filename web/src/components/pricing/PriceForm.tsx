import { useState } from "react";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import { validAmount, type PriceRow } from "@/lib/pricing";
import { CustomerPicker } from "./CustomerPicker";
import { FormFrame } from "./FormFrame";
import type { FormProps } from "./kind";
import { useBookSave, usePlants, useProducts } from "./useBook";

export function PriceForm({ item, onDone, onCancel }: FormProps) {
    const row = item as PriceRow | null;
    const products = useProducts();
    const plants = usePlants();
    const save = useBookSave("/api/prices", row?.id ?? null);
    const [productId, setProductId] = useState(row?.productId ?? "");
    const [plantId, setPlantId] = useState(row?.plantId ?? "");
    const [customerId, setCustomerId] = useState(row?.customerId ?? "");
    const [customerLabel, setCustomerLabel] = useState(row?.customerCode ?? "");
    const [price, setPrice] = useState(row ? String(row.pricePerCft) : "");
    const [from, setFrom] = useState(row?.validFrom ?? "");
    const [to, setTo] = useState(row?.validTo ?? "");

    const body = {
        productId,
        plantId: plantId || null,
        customerId: customerId || null,
        pricePerCft: Number(price),
        validFrom: from,
        validTo: to || null,
    };

    return (
        <FormFrame
            title={row ? "Edit selling price" : "New selling price"}
            canSave={
                productId !== "" &&
                validAmount(price, 2) &&
                from !== "" &&
                (!to || to >= from)
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Product">
                <select
                    value={productId}
                    onChange={(e) => setProductId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">Choose a product</option>
                    {row &&
                        !products.data?.items.some(
                            (p) => p.id === row.productId,
                        ) && (
                            <option value={row.productId}>
                                {row.productCode}
                            </option>
                        )}
                    {products.data?.items.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Price per CFT" hint="Without VAT. Up to 2 decimals.">
                <input
                    inputMode="decimal"
                    value={price}
                    onChange={(e) => setPrice(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Plant"
                hint="The most specific price wins: customer and plant, customer, plant, then general."
            >
                <select
                    value={plantId}
                    onChange={(e) => setPlantId(e.target.value)}
                    className={inputClass}
                >
                    <option value="">All plants</option>
                    {row?.plantId &&
                        !plants.data?.items.some(
                            (p) => p.id === row.plantId,
                        ) && (
                            <option value={row.plantId}>{row.plantCode}</option>
                        )}
                    {plants.data?.items.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.code} {p.name}
                        </option>
                    ))}
                </select>
            </Field>
            <Field
                label="Customer"
                hint="Leave on everyone for a general price."
            >
                <CustomerPicker
                    label={customerLabel}
                    placeholder="Everyone. Type 2 letters to pick one customer"
                    onPick={(c) => {
                        setCustomerId(c?.id ?? "");
                        setCustomerLabel(c?.code ?? "");
                    }}
                />
            </Field>
            <Field label="Valid from">
                <input
                    type="date"
                    value={from}
                    onChange={(e) => setFrom(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field label="Valid until" hint="Leave empty for no end date.">
                <input
                    type="date"
                    value={to}
                    onChange={(e) => setTo(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}
