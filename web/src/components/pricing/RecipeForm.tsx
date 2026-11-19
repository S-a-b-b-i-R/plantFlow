import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { LuPlus, LuX } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import { inputClass, textareaClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import {
    unitLabel,
    validAmount,
    type RecipeDetail,
    type RecipeRow,
} from "@/lib/pricing";
import { FormFrame } from "./FormFrame";
import type { FormProps, PriceBookRow } from "./kind";
import { useBookSave, useMaterials, usePlants, useProducts } from "./useBook";

type Line = { materialId: string; quantity: string };

const quantity = (n: number) =>
    n.toLocaleString(undefined, { maximumFractionDigits: 4 });

const useRecipe = (id: string | null) =>
    useQuery({
        queryKey: ["pricebook", "recipe", id],
        queryFn: () => api<RecipeDetail>(`/api/mix-designs/${id}`),
        enabled: !!id,
    });

// The materials of one recipe, opened from its row.
export function RecipeMaterials({ row }: { row: PriceBookRow }) {
    const recipe = useRecipe(row.id);

    if (recipe.isPending) return <Skeleton className="h-16 w-full" />;
    if (recipe.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                {recipe.error.message}
            </p>
        );
    }

    return (
        <ul className="divide-y rounded-sm border bg-muted/30 text-sm">
            {recipe.data.items.map((item) => (
                <li
                    key={item.materialId}
                    className="flex items-center justify-between gap-3 px-3 py-1.5"
                >
                    <span>
                        <span className="font-medium">{item.materialCode}</span>{" "}
                        <span className="text-muted-foreground">
                            {item.materialName}
                        </span>
                    </span>
                    <span className="tabular-nums">
                        {quantity(item.quantityPerCft)} {unitLabel[item.unit]}{" "}
                        per CFT
                    </span>
                </li>
            ))}
        </ul>
    );
}

// An existing draft loads its materials first, so the form starts with them filled in.
export function RecipeForm(props: FormProps) {
    const recipe = useRecipe(props.item?.id ?? null);

    if (props.item && recipe.isPending) {
        return <Skeleton className="h-48 w-full" />;
    }
    if (props.item && recipe.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                {recipe.error.message}
            </p>
        );
    }

    return <RecipeEditor {...props} loaded={recipe.data ?? null} />;
}

function RecipeEditor({
    item,
    loaded,
    onDone,
    onCancel,
}: FormProps & { loaded: RecipeDetail | null }) {
    const row = item as RecipeRow | null;
    const products = useProducts();
    const plants = usePlants();
    const materials = useMaterials();
    const save = useBookSave("/api/mix-designs", row?.id ?? null);
    const [productId, setProductId] = useState(row?.productId ?? "");
    const [plantId, setPlantId] = useState(row?.plantId ?? "");
    const [notes, setNotes] = useState(row?.notes ?? "");
    const [lines, setLines] = useState<Line[]>(
        loaded?.items.map((i) => ({
            materialId: i.materialId,
            quantity: String(i.quantityPerCft),
        })) ?? [{ materialId: "", quantity: "" }],
    );

    const options = materials.data?.items ?? [];
    const chosen = new Set(lines.map((l) => l.materialId).filter(Boolean));
    const setLine = (index: number, patch: Partial<Line>) =>
        setLines(lines.map((l, i) => (i === index ? { ...l, ...patch } : l)));

    const complete =
        lines.length > 0 &&
        lines.every((l) => l.materialId && validAmount(l.quantity, 4)) &&
        chosen.size === lines.length;

    const items = lines.map((l) => ({
        materialId: l.materialId,
        quantityPerCft: Number(l.quantity),
    }));
    const body = row
        ? { notes: notes.trim() || null, items }
        : {
              productId,
              plantId: plantId || null,
              notes: notes.trim() || null,
              items,
          };

    return (
        <FormFrame
            title={row ? `Edit recipe, version ${row.version}` : "New recipe"}
            canSave={(!!row || productId !== "") && complete}
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Product">
                <select
                    value={productId}
                    disabled={!!row}
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
            <Field
                label="Plant"
                hint="A plant's own recipe beats the general one. Approving a new version retires the old one."
            >
                <select
                    value={plantId}
                    disabled={!!row}
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

            <div className="space-y-2 sm:col-span-2">
                <span className="block text-sm font-medium">
                    Materials per CFT of concrete
                </span>
                {lines.map((line, index) => {
                    const unit = options.find(
                        (m) => m.id === line.materialId,
                    )?.unit;
                    return (
                        <div
                            key={index}
                            className="grid grid-cols-[1fr_8rem_2.5rem] items-center gap-2"
                        >
                            <select
                                aria-label="Material"
                                value={line.materialId}
                                onChange={(e) =>
                                    setLine(index, {
                                        materialId: e.target.value,
                                    })
                                }
                                className={inputClass}
                            >
                                <option value="">Choose a material</option>
                                {options.map((m) => (
                                    <option
                                        key={m.id}
                                        value={m.id}
                                        disabled={
                                            chosen.has(m.id) &&
                                            m.id !== line.materialId
                                        }
                                    >
                                        {m.code} {m.name}
                                    </option>
                                ))}
                            </select>
                            <span className="relative block">
                                <input
                                    aria-label="Quantity per CFT"
                                    inputMode="decimal"
                                    value={line.quantity}
                                    onChange={(e) =>
                                        setLine(index, {
                                            quantity: e.target.value,
                                        })
                                    }
                                    className={inputClass}
                                />
                                {unit && (
                                    <span className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-xs text-muted-foreground">
                                        {unitLabel[unit]}
                                    </span>
                                )}
                            </span>
                            <Button
                                type="button"
                                variant="ghost"
                                aria-label="Remove material"
                                className="size-10"
                                disabled={lines.length === 1}
                                onClick={() =>
                                    setLines(
                                        lines.filter((_, i) => i !== index),
                                    )
                                }
                            >
                                <LuX aria-hidden />
                            </Button>
                        </div>
                    );
                })}
                <Button
                    type="button"
                    variant="outline"
                    className="h-9 gap-1.5 px-3"
                    onClick={() =>
                        setLines([...lines, { materialId: "", quantity: "" }])
                    }
                >
                    <LuPlus aria-hidden /> Add material
                </Button>
            </div>

            <Field label="Notes" className="sm:col-span-2">
                <textarea
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    maxLength={1000}
                    rows={2}
                    className={textareaClass}
                />
            </Field>
        </FormFrame>
    );
}
