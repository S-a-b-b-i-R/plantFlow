export interface AuditEntry {
    id: string;
    occurredAtUtc: string;
    userId: string | null;
    userName: string | null;
    action: string;
    entityType: string;
    entityId: string;
    changesJson: string | null;
}

export interface AuditFilters {
    entityTypes: string[];
    people: { id: string; name: string }[];
}

export interface AuditQuery {
    from: string;
    to: string;
    userId: string;
    entityType: string;
    search: string;
}

export const emptyAuditQuery: AuditQuery = {
    from: "",
    to: "",
    userId: "",
    entityType: "",
    search: "",
};

export const auditParams = (query: AuditQuery) => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
        if (value.trim()) params.set(key, value.trim());
    }
    return params;
};

// "MixDesign" reads "Mix design".
export const recordTypeLabel = (type: string) => {
    const words = type.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
    return words.charAt(0).toUpperCase() + words.slice(1);
};

// "order.approval-requested" reads "Order approval requested".
export const actionLabel = (action: string) => {
    const words = action.replace(/[._-]+/g, " ").trim();
    return words.charAt(0).toUpperCase() + words.slice(1);
};

// Records with a screen of their own open from the log.
export const recordLink = (type: string, id: string) =>
    ({
        Order: `/orders/${id}`,
        Customer: `/customers/${id}`,
        Plant: `/plants/${id}`,
    })[type];

// The recorded changes as label/value pairs; anything that isn't a JSON object shows as it is.
export function changeRows(json: string | null): [string, string][] {
    if (!json) return [];
    try {
        const parsed: unknown = JSON.parse(json);
        if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
            return Object.entries(parsed).map(([key, value]) => [
                recordTypeLabel(key),
                value === null
                    ? "None"
                    : Array.isArray(value)
                      ? value.length === 0
                          ? "None"
                          : value.map(String).join(", ")
                      : typeof value === "object"
                        ? JSON.stringify(value)
                        : String(value),
            ]);
        }
    } catch {
        // Not JSON: fall through and show the raw text.
    }
    return [["Details", json]];
}
