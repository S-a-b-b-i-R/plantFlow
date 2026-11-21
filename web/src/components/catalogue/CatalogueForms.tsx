import { useState } from "react";
import { Field } from "@/components/orders/Field";
import { inputClass } from "@/components/orders/styles";
import { FormFrame } from "@/components/pricing/FormFrame";
import {
    chargeBasisLabel,
    chargeKindLabel,
    materialCategories,
    materialUnits,
    type ChargeBasis,
    type ChargeKind,
    type Material,
    type MaterialCategory,
    type MaterialUnit,
    type Product,
    type ServiceCharge,
} from "@/lib/catalogue";
import { codePattern } from "@/lib/plants";
import { useCatalogueSave } from "./useCatalogue";

const strengthPattern = /^[A-Za-z0-9 ./-]{1,20}$/;
const wholeIn = (value: string, min: number, max: number) => {
    const n = Number(value);
    return value.trim() !== "" && Number.isInteger(n) && n >= min && n <= max;
};

// Status has its own control on each row, so the forms keep the current one.
export function ProductForm({
    product,
    onDone,
    onCancel,
}: {
    product: Product | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useCatalogueSave(
        "/api/products",
        product ? `/api/products/${product.id}` : null,
    );
    const [code, setCode] = useState(product?.code ?? "");
    const [name, setName] = useState(product?.name ?? "");
    const [strength, setStrength] = useState(product?.strengthClass ?? "");
    const [aggregate, setAggregate] = useState(
        String(product?.maxAggregateMm ?? 20),
    );
    const [slump, setSlump] = useState(String(product?.defaultSlumpMm ?? 100));
    const body = {
        code: code.trim(),
        name: name.trim(),
        strengthClass: strength.trim(),
        maxAggregateMm: Number(aggregate),
        defaultSlumpMm: Number(slump),
        status: product?.status ?? "Active",
    };

    return (
        <FormFrame
            title={product ? "Edit product" : "New product"}
            saveLabel="Save product"
            canSave={
                codePattern.test(body.code) &&
                body.name.length >= 2 &&
                strengthPattern.test(body.strengthClass) &&
                wholeIn(aggregate, 5, 80) &&
                wholeIn(slump, 0, 300)
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Code" hint="Letters, numbers and dashes.">
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Strength class"
                hint="For example M25, C30 or 3000 psi."
            >
                <input
                    value={strength}
                    onChange={(e) => setStrength(e.target.value)}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Largest aggregate (mm)" hint="5 to 80.">
                <input
                    inputMode="numeric"
                    value={aggregate}
                    onChange={(e) => setAggregate(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <Field
                label="Default slump (mm)"
                hint="0 to 300. An order line can change it."
            >
                <input
                    inputMode="numeric"
                    value={slump}
                    onChange={(e) => setSlump(e.target.value)}
                    className={inputClass}
                />
            </Field>
        </FormFrame>
    );
}

export function MaterialForm({
    material,
    onDone,
    onCancel,
}: {
    material: Material | null;
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useCatalogueSave(
        "/api/materials",
        material ? `/api/materials/${material.id}` : null,
    );
    const [code, setCode] = useState(material?.code ?? "");
    const [name, setName] = useState(material?.name ?? "");
    const [category, setCategory] = useState<MaterialCategory>(
        material?.category ?? "Cement",
    );
    const [unit, setUnit] = useState<MaterialUnit>(material?.unit ?? "Kg");
    const body = {
        code: code.trim(),
        name: name.trim(),
        category,
        unit,
        status: material?.status ?? "Active",
    };

    return (
        <FormFrame
            title={material ? "Edit material" : "New material"}
            saveLabel="Save material"
            canSave={codePattern.test(body.code) && body.name.length >= 2}
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Code" hint="Letters, numbers and dashes.">
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Category">
                <select
                    value={category}
                    onChange={(e) =>
                        setCategory(e.target.value as MaterialCategory)
                    }
                    className={inputClass}
                >
                    {materialCategories.map((c) => (
                        <option key={c} value={c}>
                            {c}
                        </option>
                    ))}
                </select>
            </Field>
            <Field
                label="Unit"
                hint="Mix designs give quantities per CFT in this unit."
            >
                <select
                    value={unit}
                    onChange={(e) => setUnit(e.target.value as MaterialUnit)}
                    className={inputClass}
                >
                    {materialUnits.map((u) => (
                        <option key={u} value={u}>
                            {u}
                        </option>
                    ))}
                </select>
            </Field>
        </FormFrame>
    );
}

// A new default amount applies to orders confirmed from now on. Confirmed orders keep theirs.
export function ChargeForm({
    charge,
    usedKinds,
    onDone,
    onCancel,
}: {
    charge: ServiceCharge | null;
    // A company has one charge per kind, so kinds already taken are left out when adding.
    usedKinds: ChargeKind[];
    onDone: () => void;
    onCancel: () => void;
}) {
    const save = useCatalogueSave(
        "/api/service-charges",
        charge ? `/api/service-charges/${charge.id}` : null,
    );
    const kinds = (Object.keys(chargeKindLabel) as ChargeKind[]).filter(
        (k) => k === charge?.kind || !usedKinds.includes(k),
    );
    const [code, setCode] = useState(charge?.code ?? "");
    const [name, setName] = useState(charge?.name ?? "");
    const [kind, setKind] = useState<ChargeKind | "">(
        charge?.kind ?? kinds[0] ?? "",
    );
    const [basis, setBasis] = useState<ChargeBasis>(charge?.basis ?? "PerCft");
    const [amount, setAmount] = useState(
        charge ? String(charge.defaultAmount) : "",
    );
    const [vat, setVat] = useState(charge?.vatApplies ?? true);
    const amountValue = Number(amount);
    const body = {
        code: code.trim(),
        name: name.trim(),
        kind,
        basis,
        defaultAmount: amountValue,
        vatApplies: vat,
        status: charge?.status ?? "Active",
    };

    return (
        <FormFrame
            title={charge ? "Edit service charge" : "New service charge"}
            saveLabel="Save charge"
            canSave={
                codePattern.test(body.code) &&
                body.name.length >= 2 &&
                kind !== "" &&
                amount.trim() !== "" &&
                amountValue >= 0
            }
            save={save}
            body={body}
            onDone={onDone}
            onCancel={onCancel}
        >
            <Field label="Code" hint="Letters, numbers and dashes.">
                <input
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    maxLength={20}
                    className={inputClass}
                />
            </Field>
            <Field label="Name">
                <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={200}
                    className={inputClass}
                />
            </Field>
            <Field label="Kind" hint="One charge of each kind per company.">
                <select
                    value={kind}
                    onChange={(e) => setKind(e.target.value as ChargeKind)}
                    className={inputClass}
                    disabled={!!charge}
                >
                    {kinds.length === 0 && (
                        <option value="">Every kind is already set up</option>
                    )}
                    {kinds.map((k) => (
                        <option key={k} value={k}>
                            {chargeKindLabel[k]}
                        </option>
                    ))}
                </select>
            </Field>
            <Field label="Charged">
                <select
                    value={basis}
                    onChange={(e) => setBasis(e.target.value as ChargeBasis)}
                    className={inputClass}
                >
                    {(Object.keys(chargeBasisLabel) as ChargeBasis[]).map(
                        (b) => (
                            <option key={b} value={b}>
                                {chargeBasisLabel[b]}
                            </option>
                        ),
                    )}
                </select>
            </Field>
            <Field
                label="Default amount"
                hint="Applies to orders confirmed or amended from now on."
            >
                <input
                    inputMode="decimal"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className={inputClass}
                />
            </Field>
            <label className="flex min-h-11 items-center gap-3 self-end text-sm">
                <input
                    type="checkbox"
                    checked={vat}
                    onChange={(e) => setVat(e.target.checked)}
                    className="size-5"
                />
                VAT applies to this charge
            </label>
        </FormFrame>
    );
}
