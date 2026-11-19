import { PERMISSIONS } from "@/auth/permissions";
import { money, shortDate } from "@/lib/orders";
import {
    overheadKindLabel,
    period,
    rate,
    unitLabel,
    type BandRow,
    type MaterialCostRow,
    type OverheadRow,
    type PriceRow,
    type RecipeRow,
} from "@/lib/pricing";
import { BandForm, MaterialCostForm, OverheadForm } from "./CostForms";
import type { PriceBookKind, PriceBookRow } from "./kind";
import { PriceForm } from "./PriceForm";
import { RecipeForm, RecipeMaterials } from "./RecipeForm";

const plantText = (code: string | null) => code ?? "All plants";

const costStatuses = {
    draft: "Draft",
    pending: "PendingApproval",
    live: "Approved",
    ended: "Retired",
};

const two = (primary: string, secondary: string) => (
    <span className="min-w-0">
        <span className="block truncate font-medium">{primary}</span>
        <span className="block truncate text-xs text-muted-foreground">
            {secondary}
        </span>
    </span>
);

export const recipes: PriceBookKind = {
    key: "recipes",
    label: "Recipes",
    noun: "recipe",
    endpoint: "/api/mix-designs",
    statuses: costStatuses,
    endLabel: "Retire",
    endPath: "retire",
    see: [PERMISSIONS.mixDesignsView],
    edit: PERMISSIONS.mixDesignsEdit,
    approve: PERMISSIONS.mixDesignsApprove,
    deletable: false,
    searchHint: "Product code or name",
    grid: "md:grid-cols-[1fr_7rem_9rem_11rem]",
    head: ["Product", "Plant", "Materials"],
    cells: (item: PriceBookRow) => {
        const r = item as RecipeRow;
        return [
            two(
                `${r.productCode}, version ${r.version}`,
                r.kind === "Custom" ? "Custom recipe" : (r.notes ?? ""),
            ),
            <span key="p" className="text-sm text-muted-foreground">
                {plantText(r.plantCode)}
            </span>,
            <span key="i" className="text-sm">
                {r.itemCount} materials
            </span>,
        ];
    },
    Form: RecipeForm,
    Detail: RecipeMaterials,
};

export const prices: PriceBookKind = {
    key: "prices",
    label: "Selling prices",
    noun: "selling price",
    endpoint: "/api/prices",
    statuses: {
        draft: "Draft",
        pending: "PendingApproval",
        live: "Active",
        ended: "Expired",
    },
    endLabel: "End price",
    endPath: "expire",
    see: [PERMISSIONS.pricesView],
    edit: PERMISSIONS.pricesEdit,
    approve: PERMISSIONS.pricesApprove,
    deletable: false,
    searchHint: "Product or customer",
    grid: "md:grid-cols-[6rem_7rem_7rem_8rem_1fr_11rem]",
    head: ["Product", "Plant", "Customer", "Per CFT", "Valid"],
    cells: (item: PriceBookRow) => {
        const p = item as PriceRow;
        return [
            <span key="c" className="font-semibold">
                {p.productCode}
            </span>,
            <span key="p" className="text-sm text-muted-foreground">
                {plantText(p.plantCode)}
            </span>,
            <span key="u" className="text-sm text-muted-foreground">
                {p.customerCode ?? "Everyone"}
            </span>,
            <span key="m" className="text-sm font-medium tabular-nums">
                {money(p.pricePerCft)}
            </span>,
            <span key="v" className="text-sm">
                {period(p.validFrom, p.validTo, shortDate)}
            </span>,
        ];
    },
    Form: PriceForm,
};

export const materialCosts: PriceBookKind = {
    key: "material-costs",
    label: "Material costs",
    noun: "material cost",
    endpoint: "/api/material-costs",
    statuses: costStatuses,
    endLabel: "Retire",
    endPath: "retire",
    see: [PERMISSIONS.costsManage, PERMISSIONS.costsApprove],
    edit: PERMISSIONS.costsManage,
    approve: PERMISSIONS.costsApprove,
    deletable: true,
    searchHint: "Material code or name",
    grid: "md:grid-cols-[1fr_7rem_10rem_8rem_11rem]",
    head: ["Material", "Plant", "Cost", "Counts from"],
    cells: (item: PriceBookRow) => {
        const c = item as MaterialCostRow;
        return [
            two(c.materialCode, c.materialName),
            <span key="p" className="text-sm text-muted-foreground">
                {plantText(c.plantCode)}
            </span>,
            <span key="c" className="text-sm font-medium tabular-nums">
                {rate(c.costPerUnit)} per {unitLabel[c.unit]}
            </span>,
            <span key="d" className="text-sm">
                {shortDate(c.effectiveFrom)}
            </span>,
        ];
    },
    Form: MaterialCostForm,
};

export const overheads: PriceBookKind = {
    key: "overheads",
    label: "Overheads",
    noun: "overhead",
    endpoint: "/api/overhead-costs",
    statuses: costStatuses,
    endLabel: "Retire",
    endPath: "retire",
    see: [PERMISSIONS.costsManage, PERMISSIONS.costsApprove],
    edit: PERMISSIONS.costsManage,
    approve: PERMISSIONS.costsApprove,
    deletable: true,
    searchHint: "Name",
    grid: "md:grid-cols-[1fr_7rem_8rem_1fr_11rem]",
    head: ["Overhead", "Plant", "Per CFT", "Counts"],
    cells: (item: PriceBookRow) => {
        const o = item as OverheadRow;
        return [
            two(o.name, overheadKindLabel[o.kind]),
            <span key="p" className="text-sm text-muted-foreground">
                {plantText(o.plantCode)}
            </span>,
            <span key="a" className="text-sm font-medium tabular-nums">
                {rate(o.amountPerCft)}
            </span>,
            <span key="v" className="text-sm">
                {period(o.validFrom, o.validTo, shortDate)}
            </span>,
        ];
    },
    Form: OverheadForm,
};

export const bands: PriceBookKind = {
    key: "delivery-bands",
    label: "Delivery bands",
    noun: "delivery band",
    endpoint: "/api/delivery-charges",
    statuses: costStatuses,
    endLabel: "Retire",
    endPath: "retire",
    see: [PERMISSIONS.costsManage, PERMISSIONS.costsApprove],
    edit: PERMISSIONS.costsManage,
    approve: PERMISSIONS.costsApprove,
    deletable: true,
    searchHint: null,
    grid: "md:grid-cols-[1fr_7rem_8rem_11rem]",
    head: ["Distance", "Plant", "Per CFT"],
    cells: (item: PriceBookRow) => {
        const b = item as BandRow;
        return [
            <span key="d" className="text-sm font-medium">
                {b.toKm == null
                    ? `Over ${b.fromKm} km`
                    : `Over ${b.fromKm} km, up to ${b.toKm} km`}
            </span>,
            <span key="p" className="text-sm text-muted-foreground">
                {plantText(b.plantCode)}
            </span>,
            <span key="c" className="text-sm font-medium tabular-nums">
                {rate(b.chargePerCft)}
            </span>,
        ];
    },
    Form: BandForm,
};

// Tab order: what changes the most, first.
export const kinds: PriceBookKind[] = [
    prices,
    recipes,
    materialCosts,
    overheads,
    bands,
];
