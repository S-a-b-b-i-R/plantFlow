// Where a price book row is in its life. Recipes and costs say Approved and Retired, prices say Active and Expired.
export type Lifecycle = "draft" | "pending" | "live" | "ended";

export const lifecycleOf = (status: string): Lifecycle =>
    status === "Draft"
        ? "draft"
        : status === "PendingApproval"
          ? "pending"
          : status === "Approved" || status === "Active"
            ? "live"
            : "ended";

export const lifecycleLabel: Record<Lifecycle, string> = {
    draft: "Draft",
    pending: "Waiting for approval",
    live: "In use",
    ended: "Ended",
};

export const lifecycles = Object.keys(lifecycleLabel) as Lifecycle[];

export type MaterialUnit = "Kg" | "Litre" | "Tonne";
export type MaterialCategory =
    "Cement" | "Sand" | "Stone" | "Water" | "Admixture" | "Additive";

export const unitLabel: Record<MaterialUnit, string> = {
    Kg: "kg",
    Litre: "litre",
    Tonne: "tonne",
};

export interface MaterialOption {
    id: string;
    code: string;
    name: string;
    category: MaterialCategory;
    unit: MaterialUnit;
    status: "Active" | "Inactive";
}

interface Approvable {
    id: string;
    status: string;
    rejectionReason: string | null;
    approvedAtUtc: string | null;
}

export interface MaterialCostRow extends Approvable {
    materialId: string;
    materialCode: string;
    materialName: string;
    unit: MaterialUnit;
    plantId: string | null;
    plantCode: string | null;
    costPerUnit: number;
    effectiveFrom: string;
}

export type OverheadKind = "Overhead" | "Fuel" | "BridgeToll";

export const overheadKindLabel: Record<OverheadKind, string> = {
    Overhead: "Overhead",
    Fuel: "Fuel",
    BridgeToll: "Bridge toll",
};

export interface OverheadRow extends Approvable {
    kind: OverheadKind;
    name: string;
    plantId: string | null;
    plantCode: string | null;
    amountPerCft: number;
    validFrom: string;
    validTo: string | null;
}

export interface BandRow extends Approvable {
    plantId: string | null;
    plantCode: string | null;
    fromKm: number;
    toKm: number | null;
    chargePerCft: number;
}

export interface PriceRow extends Approvable {
    productId: string;
    productCode: string;
    plantId: string | null;
    plantCode: string | null;
    customerId: string | null;
    customerCode: string | null;
    pricePerCft: number;
    validFrom: string;
    validTo: string | null;
}

export interface RecipeRow extends Approvable {
    productId: string;
    productCode: string;
    plantId: string | null;
    plantCode: string | null;
    version: number;
    kind: "Standard" | "Custom";
    itemCount: number;
    notes: string | null;
}

export interface RecipeItem {
    materialId: string;
    materialCode: string;
    materialName: string;
    unit: MaterialUnit;
    quantityPerCft: number;
}

export interface RecipeDetail {
    design: RecipeRow;
    items: RecipeItem[];
}

export interface CustomerPick {
    id: string;
    code: string;
    name: string;
}

// A number the way people type it: at most this many decimals, and more than zero.
export const validAmount = (text: string, decimals: number) => {
    const value = Number(text);
    if (!text.trim() || !Number.isFinite(value) || value <= 0) return false;
    return Math.abs(value - Number(value.toFixed(decimals))) < 1e-9;
};

// A cost or rate with up to 4 decimals, but never fewer than 2.
export const rate = (n: number) =>
    n.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 4,
    });

// "1 Jan 2027 to no end date" reads better as a sentence than as two columns.
export const period = (
    from: string,
    to: string | null,
    format: (date: string) => string,
) => (to ? `${format(from)} to ${format(to)}` : `From ${format(from)}`);

export type PriceScope = "CustomerAndPlant" | "Customer" | "Plant" | "General";

export const scopeLabel: Record<PriceScope, string> = {
    CustomerAndPlant: "This customer at this plant",
    Customer: "This customer",
    Plant: "This plant",
    General: "General price",
};

export interface MarginRow {
    productId: string;
    productCode: string;
    productName: string;
    totalCost: number | null;
    floorPrice: number | null;
    activePrice: number | null;
    priceScope: PriceScope | null;
    marginAmount: number | null;
    marginPercent: number | null;
    belowFloor: boolean | null;
    note: string | null;
}

export interface FloorBreakdown {
    mixDesignVersion: number;
    materials: {
        materialCode: string;
        unit: MaterialUnit;
        quantityPerCft: number;
        costPerUnit: number;
        cost: number;
    }[];
    materialCost: number;
    overhead: number;
    fuel: number;
    bridgeToll: number;
    delivery: number;
    totalCost: number;
    marginPercent: number;
    aitPercent: number;
    floorPrice: number;
    vatPercent: number;
    vat: number;
    floorPriceWithVat: number;
}
