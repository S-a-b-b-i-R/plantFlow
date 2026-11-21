import type { OrderStatus, Paged } from "./orders";

export type LedgerCreditSource = "CarriedForward" | "Manual";
export type LedgerCloseReason = "Refunded" | "NotNeeded";
export type LedgerCloseStatus = "Pending" | "Approved" | "SentBack";
export type LedgerEntryKind = "Created" | "Used" | "Corrected" | "Closed";

// A customer with credits. openCft can still be used; pendingCloseCft is held for a close.
export interface LedgerCustomer {
    customerId: string;
    code: string;
    name: string;
    openCft: number;
    openValue: number;
    pendingCloseCft: number;
    credits: number;
}

// quantityCft is signed: + adds to the credit, - takes from it. A sent-back close takes nothing.
export interface LedgerEntry {
    kind: LedgerEntryKind;
    atUtc: string;
    quantityCft: number;
    text: string | null;
    orderId: string | null;
    orderNumber: string | null;
    closeStatus: LedgerCloseStatus | null;
    by: string | null;
}

export interface LedgerCredit {
    id: string;
    productId: string;
    productCode: string;
    productName: string;
    unitPrice: number;
    source: LedgerCreditSource;
    sourceOrderId: string | null;
    sourceOrderNumber: string | null;
    originalCft: number;
    balanceCft: number;
    pendingCloseCft: number;
    note: string | null;
    createdAtUtc: string;
    entries: LedgerEntry[];
}

// billedCft is final once the order is closed and provisional before that.
export interface LedgerOrder {
    orderId: string;
    number: string;
    status: OrderStatus;
    requestedDate: string;
    closedAtUtc: string | null;
    final: boolean;
    orderedCft: number;
    deliveredCft: number;
    billedCft: number;
    creditUsedCft: number;
    carriedForwardCft: number;
}

export interface LedgerStatement {
    customerId: string;
    code: string;
    name: string;
    openCft: number;
    openValue: number;
    credits: LedgerCredit[];
    orders: Paged<LedgerOrder>;
}

export interface LedgerClose {
    id: string;
    creditId: string;
    customerId: string;
    customerName: string;
    productCode: string;
    unitPrice: number;
    quantityCft: number;
    value: number;
    reason: LedgerCloseReason;
    note: string;
    status: LedgerCloseStatus;
    requestedByName: string | null;
    requestedAtUtc: string;
    requestedByMe: boolean;
    reviewedByName: string | null;
    reviewedAtUtc: string | null;
    reviewComment: string | null;
}

// An open credit an order's customer can use on one of its products.
export interface AvailableCredit {
    creditId: string;
    productId: string;
    productCode: string;
    unitPrice: number;
    balanceCft: number;
    sourceOrderNumber: string | null;
    createdAtUtc: string;
}

export const closeReasonLabel: Record<LedgerCloseReason, string> = {
    Refunded: "Refunded",
    NotNeeded: "Not needed",
};

export const closeStatusLabel: Record<LedgerCloseStatus, string> = {
    Pending: "Waiting for approval",
    Approved: "Closed",
    SentBack: "Sent back",
};

export const entryLabel: Record<LedgerEntryKind, string> = {
    Created: "Credit added",
    Used: "Used",
    Corrected: "Corrected",
    Closed: "Close",
};

// Where a credit came from, in words.
export const creditOrigin = (credit: LedgerCredit) =>
    credit.source === "CarriedForward"
        ? `Carried forward from ${credit.sourceOrderNumber ?? "an order"}`
        : "Entered by Accounts";
