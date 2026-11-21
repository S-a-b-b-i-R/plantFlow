// Plain words for each permission key, grouped the way an Admin thinks about them.
// A key the API knows but this list doesn't still shows, under "Other", by its key.
export const permissionGroups: { area: string; keys: [string, string][] }[] = [
    {
        area: "Orders",
        keys: [
            ["orders.view", "See orders"],
            ["orders.create", "Create orders"],
            ["orders.edit", "Edit and amend orders"],
            ["orders.approve.salesmanager", "Approve orders as sales manager"],
            ["orders.approve.management", "Approve orders as management"],
            ["orders.approve.technical", "Approve orders as technical"],
            ["orders.approve.credit", "Approve orders for credit"],
            ["orders.confirm", "Confirm orders"],
            ["orders.close", "Close orders"],
            ["orders.billing.resolve", "Resolve billing questions"],
        ],
    },
    {
        area: "Customers and sales",
        keys: [
            ["customers.manage", "Add and edit customers and sites"],
            ["customers.assign", "Assign customers to sales reps"],
            ["customers.vat", "Set customers as VAT exempt"],
            ["sitesurveys.view", "See site surveys"],
            ["sitesurveys.perform", "Carry out site surveys"],
            ["sitesurveys.attach", "Attach photos to site surveys"],
            ["sitesurveys.approve", "Approve site surveys"],
            ["targets.view", "See sales targets"],
            ["targets.manage", "Set sales targets"],
            ["ledger.view", "See customer ledgers"],
            ["ledger.manage", "Record ledger entries"],
            ["ledger.approve", "Approve closing ledger credits"],
        ],
    },
    {
        area: "Pricing",
        keys: [
            ["prices.view", "See prices"],
            ["prices.edit", "Draft prices"],
            ["prices.approve", "Approve prices"],
            ["costs.manage", "Record material costs and overheads"],
            ["costs.approve", "Approve costs"],
            ["mixdesigns.view", "See mix designs"],
            ["mixdesigns.edit", "Draft mix designs"],
            ["mixdesigns.approve", "Approve mix designs"],
        ],
    },
    {
        area: "Planning and delivery",
        keys: [
            ["deliveries.plan", "Plan pours"],
            ["deliveries.plan.approve", "Approve shift plans"],
            ["deliveries.plan.override", "Override planning rules"],
            ["deliveries.dispatch", "Dispatch loads"],
            ["deliveries.track", "Track loads"],
            ["batches.manage", "Run the batching console"],
            ["returns.manage", "Record returned concrete"],
            ["returns.qc", "Decide on returned concrete"],
            ["dashboards.view", "See the live board"],
        ],
    },
    {
        area: "Setup",
        keys: [
            ["masterdata.view", "See plants, customers, fleet and catalogue"],
            ["masterdata.edit", "Edit other master data"],
            ["plants.manage", "Manage plants and shifts"],
            ["fleet.manage", "Manage mixers, pumps and crews"],
            ["products.manage", "Manage products and materials"],
            ["charges.manage", "Manage service charges"],
        ],
    },
    {
        area: "Administration",
        keys: [
            ["users.manage", "Manage users"],
            ["roles.manage", "Manage roles"],
            ["settings.manage", "Change company settings"],
            ["audit.view", "See the audit log"],
        ],
    },
];

const labels = new Map(permissionGroups.flatMap((g) => g.keys));

export const permissionLabel = (key: string) => labels.get(key) ?? key;

// Every key the API lists, in the groups above, with unknown keys collected under "Other".
export function groupPermissions(all: string[]) {
    const known = new Set(labels.keys());
    const groups = permissionGroups
        .map((g) => ({
            area: g.area,
            keys: g.keys.map(([key]) => key).filter((key) => all.includes(key)),
        }))
        .filter((g) => g.keys.length > 0);
    const other = all.filter((key) => !known.has(key));
    return other.length > 0
        ? [...groups, { area: "Other", keys: other }]
        : groups;
}
