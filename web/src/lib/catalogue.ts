import type { ActiveStatus } from "@/lib/plants";

export interface Product {
    id: string;
    code: string;
    name: string;
    strengthClass: string;
    maxAggregateMm: number;
    defaultSlumpMm: number;
    status: ActiveStatus;
}

export type MaterialCategory =
    "Cement" | "Sand" | "Stone" | "Water" | "Admixture" | "Additive";

export const materialCategories: MaterialCategory[] = [
    "Cement",
    "Sand",
    "Stone",
    "Water",
    "Admixture",
    "Additive",
];

export type MaterialUnit = "Kg" | "Litre" | "Tonne";

export const materialUnits: MaterialUnit[] = ["Kg", "Litre", "Tonne"];

export interface Material {
    id: string;
    code: string;
    name: string;
    category: MaterialCategory;
    unit: MaterialUnit;
    status: ActiveStatus;
}

export type ChargeKind =
    "Pump" | "NightDelivery" | "ShortLoad" | "ExtraDistance";

export const chargeKindLabel: Record<ChargeKind, string> = {
    Pump: "Pump",
    NightDelivery: "Night delivery",
    ShortLoad: "Short load",
    ExtraDistance: "Extra distance",
};

export type ChargeBasis = "PerCft" | "PerLoad" | "Fixed";

export const chargeBasisLabel: Record<ChargeBasis, string> = {
    PerCft: "per CFT",
    PerLoad: "per load",
    Fixed: "per order",
};

export interface ServiceCharge {
    id: string;
    code: string;
    name: string;
    kind: ChargeKind;
    basis: ChargeBasis;
    defaultAmount: number;
    status: ActiveStatus;
    vatApplies: boolean;
}

export interface ProductUsage {
    activePrices: number;
    approvedMixDesigns: number;
    openOrders: number;
}

export interface MaterialUsage {
    approvedMixDesigns: number;
}

const count = (n: number, one: string, many: string) =>
    n === 1 ? `1 ${one}` : `${n} ${many}`;

// What still uses the record, in words, or null when nothing does.
export function usageText(usage: ProductUsage | MaterialUsage) {
    const parts: string[] = [];
    if ("activePrices" in usage && usage.activePrices > 0) {
        parts.push(count(usage.activePrices, "active price", "active prices"));
    }
    if (usage.approvedMixDesigns > 0) {
        parts.push(
            count(
                usage.approvedMixDesigns,
                "approved mix design",
                "approved mix designs",
            ),
        );
    }
    if ("openOrders" in usage && usage.openOrders > 0) {
        parts.push(count(usage.openOrders, "open order", "open orders"));
    }
    return parts.length > 0 ? parts.join(", ") : null;
}

export const productBody = (product: Product, status: ActiveStatus) => ({
    code: product.code,
    name: product.name,
    strengthClass: product.strengthClass,
    maxAggregateMm: product.maxAggregateMm,
    defaultSlumpMm: product.defaultSlumpMm,
    status,
});

export const materialBody = (material: Material, status: ActiveStatus) => ({
    code: material.code,
    name: material.name,
    category: material.category,
    unit: material.unit,
    status,
});

export const chargeBody = (charge: ServiceCharge, status: ActiveStatus) => ({
    code: charge.code,
    name: charge.name,
    kind: charge.kind,
    basis: charge.basis,
    defaultAmount: charge.defaultAmount,
    vatApplies: charge.vatApplies,
    status,
});
