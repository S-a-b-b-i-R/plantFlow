import type { ActiveStatus } from "@/lib/plants";

export type CustomerType = "Contractor" | "Developer" | "Individual";

export const customerTypes: CustomerType[] = [
    "Contractor",
    "Developer",
    "Individual",
];

export interface Customer {
    id: string;
    code: string;
    name: string;
    type: CustomerType;
    phone: string | null;
    email: string | null;
    salesRepId: string | null;
    salesRepName: string | null;
    status: ActiveStatus;
    siteCount: number;
    vatExempt: boolean;
}

export interface Site {
    id: string;
    customerId: string;
    name: string;
    address: string | null;
    latitude: number | null;
    longitude: number | null;
    contactName: string | null;
    contactPhone: string | null;
    accessNotes: string | null;
    status: ActiveStatus;
}

export interface SiteDistance {
    plantId: string;
    plantCode: string;
    distanceKm: number;
}

export interface SalesRep {
    id: string;
    name: string;
    region: string | null;
    userId: string | null;
    managerId: string | null;
    status: ActiveStatus;
    userEmail: string | null;
}

export interface LinkableUser {
    id: string;
    email: string;
    displayName: string | null;
    linkedRepId: string | null;
}

export interface OpenOrders {
    openOrders: number;
}

// The body PUT /api/customers/{id} expects, built from a customer with some fields changed.
export const customerBody = (
    customer: Customer,
    changes: Partial<{
        code: string;
        name: string;
        type: CustomerType;
        phone: string | null;
        email: string | null;
        salesRepId: string | null;
        status: ActiveStatus;
    }> = {},
) => ({
    code: customer.code,
    name: customer.name,
    type: customer.type,
    phone: customer.phone,
    email: customer.email,
    salesRepId: customer.salesRepId,
    status: customer.status,
    vatExempt: customer.vatExempt,
    ...changes,
});

export const siteBody = (
    site: Site,
    changes: Partial<Omit<Site, "id" | "customerId">> = {},
) => ({
    name: site.name,
    address: site.address,
    latitude: site.latitude,
    longitude: site.longitude,
    contactName: site.contactName,
    contactPhone: site.contactPhone,
    accessNotes: site.accessNotes,
    status: site.status,
    ...changes,
});

export const openOrdersText = (n: number) =>
    n === 1 ? "1 open order" : `${n} open orders`;
