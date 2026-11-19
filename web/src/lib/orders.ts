// Shapes and wording for orders. The field names are the API's.

export type OrderStatus =
    | "Draft"
    | "Submitted"
    | "InApproval"
    | "Approved"
    | "Confirmed"
    | "InProgress"
    | "Completed"
    | "OnHold"
    | "Rejected"
    | "Cancelled";

export type PourElement =
    "Slab" | "Column" | "Footing" | "GroundLaying" | "Road" | "Other";
export type PourMethod = "Chute" | "Pump" | "Crane";
export type ConfirmationMethod =
    "Phone" | "Email" | "WhatsApp" | "WrittenPo" | "SignedQuotation";
export type ApprovalStage =
    "SalesManager" | "Management" | "Technical" | "Credit";
export type ApprovalDecision =
    "Pending" | "Approved" | "Rejected" | "Withdrawn";
export type BillingBasis = "Delivered" | "Ordered" | "Custom";
export type AdjustmentKind = "Return" | "QcReject" | "Discount" | "Other";

export interface Paged<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
}

export interface OrderSummary {
    id: string;
    number: string;
    customerId: string;
    customerName: string;
    siteId: string;
    siteName: string;
    salesRepId: string | null;
    salesRepName: string | null;
    plantId: string;
    plantCode: string;
    requestedDate: string;
    distanceKm: number;
    status: OrderStatus;
    paymentTerms: string | null;
    notes: string | null;
    cancelReason: string | null;
    lineCount: number;
    totalQuantityCft: number;
    totalValue: number;
    belowFloorLineCount: number;
    confirmedAtUtc: string | null;
    confirmationMethod: ConfirmationMethod | null;
    confirmationReference: string | null;
    holdReason: string | null;
    surveyId: string | null;
}

export interface OrderLine {
    id: string;
    productId: string;
    productCode: string;
    productName: string;
    quantityCft: number;
    unitPrice: number;
    listPrice: number | null;
    floorPrice: number | null;
    belowFloor: boolean;
    slumpMm: number;
    pourElement: PourElement;
    pourMethod: PourMethod;
    notes: string | null;
    lineValue: number;
}

export interface OrderHistory {
    fromStatus: OrderStatus | null;
    toStatus: OrderStatus;
    changedAtUtc: string;
    reason: string | null;
}

export interface OrderApproval {
    stage: ApprovalStage;
    decision: ApprovalDecision;
    round: number;
    openedAtUtc: string;
    decidedAtUtc: string | null;
    comment: string | null;
}

export interface OrderCharge {
    kind: string;
    description: string;
    quantityCft: number;
    ratePerCft: number;
    amount: number;
    vatApplies: boolean;
}

export interface OrderDetail {
    order: OrderSummary;
    lines: OrderLine[];
    history: OrderHistory[];
    approvals: OrderApproval[];
    charges: OrderCharge[];
}

export interface InvoiceLine {
    lineId: string;
    productCode: string;
    productName: string;
    orderedCft: number;
    deliveredCft: number;
    billedCft: number | null;
    basis: BillingBasis | null;
    note: string | null;
    needsResolution: boolean;
    unitPrice: number;
    amount: number;
}

export interface InvoiceAdjustment {
    id: string;
    kind: AdjustmentKind;
    reason: string;
    quantityCft: number | null;
    amount: number;
    createdAtUtc: string;
}

export interface Invoice {
    orderId: string;
    number: string;
    status: OrderStatus;
    final: boolean;
    lines: InvoiceLine[];
    charges: OrderCharge[];
    adjustments: InvoiceAdjustment[];
    concreteAmount: number;
    chargesAmount: number;
    adjustmentsAmount: number;
    subtotal: number;
    taxableAmount: number;
    vatExempt: boolean;
    vatPercent: number;
    vatAmount: number;
    total: number;
    canClose: boolean;
}

export interface CustomerOption {
    id: string;
    code: string;
    name: string;
    salesRepId: string | null;
    salesRepName: string | null;
    status: "Active" | "Inactive";
}

export interface SiteOption {
    id: string;
    name: string;
    address: string | null;
    status: "Active" | "Inactive";
}

export interface ProductOption {
    id: string;
    code: string;
    name: string;
    strengthClass: string;
    defaultSlumpMm: number;
    status: "Active" | "Inactive";
}

export interface PlantOption {
    id: string;
    code: string;
    name: string;
    status: "Active" | "Inactive";
}

export interface SalesRepOption {
    id: string;
    name: string;
    status: "Active" | "Inactive";
}

export interface SurveyOption {
    id: string;
    siteName: string;
    surveyedOn: string;
    result: string;
}

export interface PriceMatch {
    priceId: string;
    pricePerCft: number;
}

export const statusLabel: Record<OrderStatus, string> = {
    Draft: "Draft",
    Submitted: "Waiting for Sales Manager",
    InApproval: "In approval",
    Approved: "Approved",
    Confirmed: "Confirmed",
    InProgress: "Delivering",
    Completed: "Completed",
    OnHold: "On hold",
    Rejected: "Rejected",
    Cancelled: "Cancelled",
};

// The colour a status chip takes. Settled good states are green, stopped states are red.
export const statusTone = (status: OrderStatus) =>
    status === "Completed" || status === "Confirmed" || status === "Approved"
        ? "ok"
        : status === "Cancelled" || status === "Rejected" || status === "OnHold"
          ? "late"
          : status === "Draft"
            ? "quiet"
            : "ink";

export const stageLabel: Record<ApprovalStage, string> = {
    SalesManager: "Sales Manager",
    Management: "Management",
    Technical: "Technical",
    Credit: "Credit",
};

export const elementLabel: Record<PourElement, string> = {
    Slab: "Slab",
    Column: "Column",
    Footing: "Footing",
    GroundLaying: "Ground laying",
    Road: "Road",
    Other: "Other",
};

export const methodLabel: Record<PourMethod, string> = {
    Chute: "Chute",
    Pump: "Pump",
    Crane: "Crane",
};

export const confirmationLabel: Record<ConfirmationMethod, string> = {
    Phone: "Phone call",
    Email: "Email",
    WhatsApp: "WhatsApp",
    WrittenPo: "Written purchase order",
    SignedQuotation: "Signed quotation",
};

export const basisLabel: Record<BillingBasis, string> = {
    Delivered: "What was delivered",
    Ordered: "What was ordered",
    Custom: "Another quantity",
};

export const adjustmentLabel: Record<AdjustmentKind, string> = {
    Return: "Returned concrete",
    QcReject: "Rejected by QC",
    Discount: "Discount",
    Other: "Other",
};

export const money = (n: number) =>
    n.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    });

export const shortDate = (date: string) =>
    new Date(`${date}T12:00:00`).toLocaleDateString([], {
        day: "numeric",
        month: "short",
        year: "numeric",
    });

export const dateTime = (iso: string) =>
    new Date(iso).toLocaleString([], {
        day: "numeric",
        month: "short",
        hour: "2-digit",
        minute: "2-digit",
    });

// Why a draft is back with the rep: the reason on the last move into Draft from another status.
export const sentBackReason = (history: OrderHistory[]) =>
    [...history]
        .filter((h) => h.toStatus === "Draft" && h.fromStatus !== null)
        .sort((a, b) => b.changedAtUtc.localeCompare(a.changedAtUtc))[0]
        ?.reason ?? null;
