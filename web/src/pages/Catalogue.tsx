import { useState, type ReactNode } from "react";
import { LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import {
    ChargeForm,
    MaterialForm,
    ProductForm,
} from "@/components/catalogue/CatalogueForms";
import {
    useCatalogueSave,
    useCharges,
    useMaterials,
    useProducts,
    useUsage,
} from "@/components/catalogue/useCatalogue";
import { OpButton as Button } from "@/components/operator/OpButton";
import {
    nextButtonClass,
    panelClass,
    tabClass,
    tabRowClass,
} from "@/components/orders/styles";
import { StatusChip } from "@/components/plants/StatusChip";
import { Skeleton } from "@/components/ui/skeleton";
import {
    chargeBasisLabel,
    chargeBody,
    chargeKindLabel,
    materialBody,
    productBody,
    usageText,
    type Material,
    type MaterialUsage,
    type Product,
    type ProductUsage,
    type ServiceCharge,
} from "@/lib/catalogue";
import { money } from "@/lib/orders";
import type { ActiveStatus } from "@/lib/plants";
import { cn } from "@/lib/utils";

type Tab = "products" | "materials" | "charges";

const tabs: { id: Tab; label: string }[] = [
    { id: "products", label: "Products" },
    { id: "materials", label: "Materials" },
    { id: "charges", label: "Service charges" },
];

// What the company sells (concrete grades), what goes into it (materials), and the extra fees it charges.
export function Catalogue() {
    const me = useMe().data!;
    const canProducts = can(me, PERMISSIONS.productsManage);
    const canCharges = can(me, PERMISSIONS.chargesManage);
    const [tab, setTab] = useState<Tab>("products");

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Catalogue</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    The concrete grades you sell, the materials in them, and
                    your service charges.
                </p>
            </header>

            <div role="tablist" aria-label="Catalogue" className={tabRowClass}>
                {tabs.map((t) => (
                    <button
                        key={t.id}
                        type="button"
                        role="tab"
                        aria-selected={tab === t.id}
                        onClick={() => setTab(t.id)}
                        className={tabClass(tab === t.id)}
                    >
                        {t.label}
                    </button>
                ))}
            </div>

            {tab === "products" && <ProductTab canManage={canProducts} />}
            {tab === "materials" && <MaterialTab canManage={canProducts} />}
            {tab === "charges" && <ChargeTab canManage={canCharges} />}
        </div>
    );
}

function TabHeader({
    text,
    newLabel,
    onNew,
}: {
    text: string;
    newLabel: string | null;
    onNew: () => void;
}) {
    return (
        <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm text-muted-foreground">{text}</p>
            {newLabel && (
                <Button
                    className={cn("h-11 px-4", nextButtonClass)}
                    onClick={onNew}
                >
                    <LuPlus aria-hidden /> {newLabel}
                </Button>
            )}
        </div>
    );
}

function ListState({
    loading,
    error,
    empty,
    emptyText,
}: {
    loading: boolean;
    error: Error | null;
    empty: boolean;
    emptyText: string;
}) {
    if (loading) return <Skeleton className="h-40 w-full" />;
    if (error) {
        return (
            <p role="alert" className="text-sm text-late">
                {error.message}
            </p>
        );
    }
    return empty ? (
        <p className="text-sm text-muted-foreground">{emptyText}</p>
    ) : null;
}

function ProductTab({ canManage }: { canManage: boolean }) {
    const products = useProducts();
    const [editing, setEditing] = useState<Product | "new" | null>(null);
    const items = products.data?.items ?? [];
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <TabHeader
                text="Concrete grades. Prices and mix designs are set per product on the Pricing screen."
                newLabel={canManage && editing === null ? "New product" : null}
                onNew={() => setEditing("new")}
            />
            {editing !== null && (
                <ProductForm
                    key={editing === "new" ? "new" : editing.id}
                    product={editing === "new" ? null : editing}
                    onDone={close}
                    onCancel={close}
                />
            )}
            <ListState
                loading={products.isLoading}
                error={products.error}
                empty={!!products.data && items.length === 0}
                emptyText="No products yet."
            />
            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((p) => (
                        <Row
                            key={p.id}
                            title={`${p.code} · ${p.name}`}
                            line={`${p.strengthClass} · ${p.maxAggregateMm} mm aggregate · ${p.defaultSlumpMm} mm slump`}
                            status={p.status}
                            canManage={canManage}
                            onEdit={() => setEditing(p)}
                            control={
                                <StatusControl<ProductUsage>
                                    noun="product"
                                    status={p.status}
                                    usagePath={`/api/products/${p.id}/usage`}
                                    savePath={`/api/products/${p.id}`}
                                    bodyFor={(s) => productBody(p, s)}
                                />
                            }
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function MaterialTab({ canManage }: { canManage: boolean }) {
    const materials = useMaterials();
    const [editing, setEditing] = useState<Material | "new" | null>(null);
    const items = materials.data?.items ?? [];
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <TabHeader
                text="What goes into the concrete. Costs are set on the Pricing screen."
                newLabel={canManage && editing === null ? "New material" : null}
                onNew={() => setEditing("new")}
            />
            {editing !== null && (
                <MaterialForm
                    key={editing === "new" ? "new" : editing.id}
                    material={editing === "new" ? null : editing}
                    onDone={close}
                    onCancel={close}
                />
            )}
            <ListState
                loading={materials.isLoading}
                error={materials.error}
                empty={!!materials.data && items.length === 0}
                emptyText="No materials yet."
            />
            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((m) => (
                        <Row
                            key={m.id}
                            title={`${m.code} · ${m.name}`}
                            line={`${m.category} · measured in ${m.unit}`}
                            status={m.status}
                            canManage={canManage}
                            onEdit={() => setEditing(m)}
                            control={
                                <StatusControl<MaterialUsage>
                                    noun="material"
                                    status={m.status}
                                    usagePath={`/api/materials/${m.id}/usage`}
                                    savePath={`/api/materials/${m.id}`}
                                    bodyFor={(s) => materialBody(m, s)}
                                />
                            }
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function ChargeTab({ canManage }: { canManage: boolean }) {
    const charges = useCharges();
    const [editing, setEditing] = useState<ServiceCharge | "new" | null>(null);
    const items = charges.data?.items ?? [];
    const usedKinds = items.map((c) => c.kind);
    const allKindsUsed =
        usedKinds.length >= Object.keys(chargeKindLabel).length;
    const close = () => setEditing(null);

    return (
        <section className="space-y-3">
            <TabHeader
                text="Extra fees added to orders. A new amount applies to orders confirmed or amended from then on."
                newLabel={
                    canManage && editing === null && !allKindsUsed
                        ? "New charge"
                        : null
                }
                onNew={() => setEditing("new")}
            />
            {editing !== null && (
                <ChargeForm
                    key={editing === "new" ? "new" : editing.id}
                    charge={editing === "new" ? null : editing}
                    usedKinds={usedKinds}
                    onDone={close}
                    onCancel={close}
                />
            )}
            <ListState
                loading={charges.isLoading}
                error={charges.error}
                empty={!!charges.data && items.length === 0}
                emptyText="No service charges yet."
            />
            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((c) => (
                        <Row
                            key={c.id}
                            title={`${c.code} · ${c.name}`}
                            line={`${chargeKindLabel[c.kind]} · ${money(c.defaultAmount)} ${chargeBasisLabel[c.basis]} · ${c.vatApplies ? "VAT applies" : "No VAT"}`}
                            status={c.status}
                            canManage={canManage}
                            onEdit={() => setEditing(c)}
                            control={
                                <StatusControl
                                    noun="charge"
                                    status={c.status}
                                    usagePath={null}
                                    savePath={`/api/service-charges/${c.id}`}
                                    bodyFor={(s) => chargeBody(c, s)}
                                />
                            }
                        />
                    ))}
                </ul>
            )}
        </section>
    );
}

function Row({
    title,
    line,
    status,
    canManage,
    onEdit,
    control,
}: {
    title: string;
    line: string;
    status: ActiveStatus;
    canManage: boolean;
    onEdit: () => void;
    control: ReactNode;
}) {
    return (
        <li className="space-y-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {title}
                        {status === "Inactive" && (
                            <StatusChip status={status} />
                        )}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                        {line}
                    </span>
                </div>
                {canManage && (
                    <Button variant="outline" className="h-11" onClick={onEdit}>
                        Edit
                    </Button>
                )}
            </div>
            {canManage && control}
        </li>
    );
}

// Deactivate first shows what still uses the record (when there is a usage check), then saves.
// Reactivate is one click.
function StatusControl<T extends ProductUsage | MaterialUsage>({
    noun,
    status,
    usagePath,
    savePath,
    bodyFor,
}: {
    noun: string;
    status: ActiveStatus;
    usagePath: string | null;
    savePath: string;
    bodyFor: (status: ActiveStatus) => unknown;
}) {
    const [confirming, setConfirming] = useState(false);
    const usage = useUsage<T>(usagePath ?? "", confirming && !!usagePath);
    const save = useCatalogueSave("", savePath);
    const setStatus = (next: ActiveStatus) =>
        save.mutate(bodyFor(next), { onSuccess: () => setConfirming(false) });
    const error = save.isError && (
        <p role="alert" className="text-sm text-late">
            {save.error.message}
        </p>
    );

    if (status === "Inactive") {
        return (
            <div className="space-y-2">
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={save.isPending}
                    onClick={() => setStatus("Active")}
                >
                    Reactivate {noun}
                </Button>
                {error}
            </div>
        );
    }

    if (!confirming) {
        return (
            <Button
                variant="outline"
                tone="danger"
                className="h-11"
                onClick={() => {
                    save.reset();
                    setConfirming(true);
                }}
            >
                Deactivate {noun}
            </Button>
        );
    }

    const inUse = usage.data ? usageText(usage.data) : null;
    return (
        <div className="space-y-2 rounded-sm border bg-muted/40 p-3 text-sm">
            {usagePath && usage.isLoading && <Skeleton className="h-5 w-48" />}
            {usage.isError && (
                <p role="alert" className="text-late">
                    {usage.error.message}
                </p>
            )}
            {(!usagePath || usage.data) && (
                <p>
                    {inUse
                        ? `This ${noun} is still used by ${inUse}. They keep it, but it can't be picked for anything new.`
                        : `Nothing active uses this ${noun}. It can't be picked for anything new once deactivated.`}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    tone="danger"
                    className="h-11 px-4"
                    disabled={save.isPending || (!!usagePath && !usage.data)}
                    onClick={() => setStatus("Inactive")}
                >
                    Deactivate {noun}
                </Button>
                <Button
                    variant="outline"
                    className="h-11 px-4"
                    onClick={() => setConfirming(false)}
                >
                    Never mind
                </Button>
            </div>
            {error}
        </div>
    );
}
