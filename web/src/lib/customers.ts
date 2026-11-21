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
    // Open: no owner, or the owner's days ran out. Any rep can sell to it.
    isOpen: boolean;
    // When the owner's days run out. Null with no owner or when the company setting is off.
    opensAtUtc: string | null;
}

// Mine = owned by the signed-in rep. Open = any rep can take it with a confirmed order.
export type OwnershipFilter = "All" | "Mine" | "Open";

// One line about who owns the customer and for how long.
export function ownershipText(customer: Customer, myRepId: string | null) {
    if (customer.isOpen) {
        return customer.salesRepName
            ? `Open: ${customer.salesRepName}'s time ran out. The first rep to get an order confirmed takes it.`
            : "Open: the first rep to get an order confirmed takes it.";
    }
    const owns =
        customer.salesRepId === myRepId
            ? "You own"
            : `${customer.salesRepName} owns`;
    if (!customer.opensAtUtc) return `${owns} this customer.`;
    const until = new Date(customer.opensAtUtc).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
    return `${owns} this customer until ${until}. Each confirmed order extends it.`;
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
