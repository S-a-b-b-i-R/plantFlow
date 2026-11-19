import type { OrderDetail, PourElement, PourMethod } from "@/lib/orders";

export type LineDraft = {
    key: string;
    id?: string;
    productId: string;
    quantity: string;
    unitPrice: string;
    slump: string;
    element: PourElement;
    method: PourMethod;
    notes: string;
};

export type Draft = {
    customerId: string;
    customerName: string;
    siteId: string;
    plantId: string;
    date: string;
    distance: string;
    salesRepId: string;
    paymentTerms: string;
    notes: string;
    surveyId: string;
    requestSurvey: boolean;
    lines: LineDraft[];
};

export const blankLine = (key: string): LineDraft => ({
    key,
    productId: "",
    quantity: "",
    unitPrice: "",
    slump: "",
    element: "Slab",
    method: "Pump",
    notes: "",
});

export const emptyDraft = (): Draft => ({
    customerId: "",
    customerName: "",
    siteId: "",
    plantId: "",
    date: "",
    distance: "",
    salesRepId: "",
    paymentTerms: "",
    notes: "",
    surveyId: "",
    requestSurvey: false,
    lines: [blankLine("n0")],
});

export const draftFrom = ({ order, lines }: OrderDetail): Draft => ({
    customerId: order.customerId,
    customerName: order.customerName,
    siteId: order.siteId,
    plantId: order.plantId,
    date: order.requestedDate,
    distance: String(order.distanceKm),
    salesRepId: order.salesRepId ?? "",
    paymentTerms: order.paymentTerms ?? "",
    notes: order.notes ?? "",
    surveyId: order.surveyId ?? "",
    requestSurvey: order.surveyRequested,
    lines: lines.map((l) => ({
        key: l.id,
        id: l.id,
        productId: l.productId,
        quantity: String(l.quantityCft),
        unitPrice: String(l.unitPrice),
        slump: String(l.slumpMm),
        element: l.pourElement,
        method: l.pourMethod,
        notes: l.notes ?? "",
    })),
});

const twoDecimals = (value: number) => Math.round(value * 100) === value * 100;

export const parseAmount = (text: string) => {
    const value = Number(text);
    return text.trim() !== "" && Number.isFinite(value) ? value : null;
};

export const validQuantity = (text: string) => {
    const value = parseAmount(text);
    return value !== null && value > 0 && twoDecimals(value);
};

export const validPrice = (text: string) => {
    if (text.trim() === "") return true;
    const value = parseAmount(text);
    return value !== null && value > 0 && twoDecimals(value);
};

// The body the API takes for create, update, revise and amend.
export const toRequest = (draft: Draft) => ({
    customerId: draft.customerId,
    siteId: draft.siteId,
    salesRepId: draft.salesRepId || undefined,
    plantId: draft.plantId,
    requestedDate: draft.date,
    distanceKm: parseAmount(draft.distance) ?? undefined,
    paymentTerms: draft.paymentTerms.trim() || undefined,
    notes: draft.notes.trim() || undefined,
    surveyId: draft.surveyId || undefined,
    requestSurvey: draft.requestSurvey,
    lines: draft.lines.map((l) => ({
        id: l.id,
        productId: l.productId,
        quantityCft: Number(l.quantity),
        unitPrice: parseAmount(l.unitPrice) ?? undefined,
        slumpMm: parseAmount(l.slump) ?? undefined,
        pourElement: l.element,
        pourMethod: l.method,
        notes: l.notes.trim() || undefined,
    })),
});
