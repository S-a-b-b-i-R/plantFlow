import type { ComponentType, ReactNode } from "react";
import type { Lifecycle } from "@/lib/pricing";

export interface PriceBookRow {
    id: string;
    status: string;
    rejectionReason: string | null;
}

export interface FormProps {
    // Null means a new row.
    item: PriceBookRow | null;
    onDone: () => void;
    onCancel: () => void;
}

// Everything that differs between recipes, prices, costs, overheads and delivery bands.
// The list, the row actions and the approvals inbox are the same for all of them.
export interface PriceBookKind {
    key: string;
    label: string;
    noun: string;
    endpoint: string;
    // The status the API filters on for each stage of a row's life.
    statuses: Record<Lifecycle, string>;
    // What ending a row in use is called, and the endpoint that does it.
    endLabel: string;
    endPath: "retire" | "expire";
    // Any one of these lets someone open the tab.
    see: string[];
    edit: string;
    approve: string;
    deletable: boolean;
    searchHint: string | null;
    grid: string;
    head: string[];
    cells: (row: PriceBookRow) => ReactNode[];
    Form: ComponentType<FormProps>;
    Detail?: ComponentType<{ row: PriceBookRow }>;
}
