import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { LuChevronLeft, LuMapPin, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import {
    CustomerForm,
    DistanceEditor,
    SiteForm,
} from "@/components/customers/CustomerForms";
import { DeactivateControl } from "@/components/customers/DeactivateControl";
import {
    useActivePlantOptions,
    useCustomer,
    useCustomerSave,
    useSiteDistances,
    useSites,
} from "@/components/customers/useCustomers";
import { OpButton as Button } from "@/components/operator/OpButton";
import { panelClass } from "@/components/orders/styles";
import { StatusChip } from "@/components/plants/StatusChip";
import { Skeleton } from "@/components/ui/skeleton";
import {
    customerBody,
    siteBody,
    type Customer,
    type Site,
} from "@/lib/customers";
import { mapLink } from "@/lib/surveys";
import { cn } from "@/lib/utils";

type Editing =
    | { kind: "customer" }
    | { kind: "site"; site: Site | null }
    | { kind: "distances"; siteId: string }
    | null;

// One customer: details, VAT exemption and the sites they pour at. Only one form is open at a time.
export function CustomerPage() {
    const { id = "" } = useParams();
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.customersManage);
    const canAssign = can(me, PERMISSIONS.customersAssign);
    const canVat = can(me, PERMISSIONS.customersVat);
    const customer = useCustomer(id);
    const sites = useSites(id);
    const [editing, setEditing] = useState<Editing>(null);
    const close = () => setEditing(null);

    if (customer.isLoading) {
        return (
            <div className="mx-auto max-w-3xl">
                <Skeleton className="h-60 w-full" />
            </div>
        );
    }
    if (customer.isError || !customer.data) {
        return (
            <div className="mx-auto max-w-3xl space-y-3">
                <BackLink />
                <p role="alert" className="text-sm text-late">
                    {customer.error?.message ?? "This customer was not found."}
                </p>
            </div>
        );
    }

    const c = customer.data;
    const siteItems = sites.data?.items ?? [];

    return (
        <div className="mx-auto max-w-3xl space-y-6">
            <div className="space-y-3">
                <BackLink />
                <header className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                        <p className="text-sm font-semibold text-muted-foreground">
                            {c.code} · {c.type}
                        </p>
                        <h1 className="display flex flex-wrap items-center gap-3 text-4xl leading-none">
                            {c.name}
                            <StatusChip status={c.status} />
                        </h1>
                        <dl className="mt-2 grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
                            <dt className="text-muted-foreground">Sales rep</dt>
                            <dd>{c.salesRepName ?? "None"}</dd>
                            {c.phone && (
                                <>
                                    <dt className="text-muted-foreground">
                                        Phone
                                    </dt>
                                    <dd>
                                        <a
                                            href={`tel:${c.phone}`}
                                            className="underline"
                                        >
                                            {c.phone}
                                        </a>
                                    </dd>
                                </>
                            )}
                            {c.email && (
                                <>
                                    <dt className="text-muted-foreground">
                                        Email
                                    </dt>
                                    <dd className="break-all">{c.email}</dd>
                                </>
                            )}
                            <dt className="text-muted-foreground">VAT</dt>
                            <dd>{c.vatExempt ? "Exempt" : "Charged"}</dd>
                        </dl>
                    </div>
                    {canManage && editing?.kind !== "customer" && (
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={() => setEditing({ kind: "customer" })}
                        >
                            Edit customer
                        </Button>
                    )}
                </header>
                {editing?.kind === "customer" && (
                    <CustomerForm
                        customer={c}
                        canAssign={canAssign}
                        onDone={close}
                        onCancel={close}
                    />
                )}
                <div className="flex flex-wrap items-start gap-2">
                    {canVat && <VatExemptToggle customer={c} />}
                    {canManage && editing?.kind !== "customer" && (
                        <DeactivateControl
                            noun="customer"
                            status={c.status}
                            usagePath={`/api/customers/${c.id}/usage`}
                            savePath={`/api/customers/${c.id}`}
                            bodyFor={(status) => customerBody(c, { status })}
                        />
                    )}
                </div>
            </div>

            <section className="space-y-3">
                <div className="flex flex-wrap items-end justify-between gap-3">
                    <div>
                        <h2 className="display text-2xl leading-none">Sites</h2>
                        <p className="mt-1 text-sm text-muted-foreground">
                            Where this customer pours. Each order is for one
                            site.
                        </p>
                    </div>
                    {canManage && editing?.kind !== "site" && (
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={() =>
                                setEditing({ kind: "site", site: null })
                            }
                        >
                            <LuPlus aria-hidden /> Add site
                        </Button>
                    )}
                </div>
                {editing?.kind === "site" && (
                    <SiteForm
                        key={editing.site?.id ?? "new"}
                        customerId={c.id}
                        site={editing.site}
                        onDone={close}
                        onCancel={close}
                    />
                )}
                {sites.isLoading && <Skeleton className="h-24 w-full" />}
                {sites.isError && (
                    <p role="alert" className="text-sm text-late">
                        {sites.error.message}
                    </p>
                )}
                {sites.data && siteItems.length === 0 && (
                    <p className="text-sm text-muted-foreground">
                        No sites yet. An order needs a site.
                    </p>
                )}
                {siteItems.length > 0 && (
                    <ul className={cn(panelClass, "divide-y")}>
                        {siteItems.map((site) => (
                            <SiteRow
                                key={site.id}
                                site={site}
                                canManage={canManage}
                                editingDistances={
                                    editing?.kind === "distances" &&
                                    editing.siteId === site.id
                                }
                                onEdit={() =>
                                    setEditing({ kind: "site", site })
                                }
                                onDistances={() =>
                                    setEditing({
                                        kind: "distances",
                                        siteId: site.id,
                                    })
                                }
                                onCloseDistances={close}
                            />
                        ))}
                    </ul>
                )}
            </section>
        </div>
    );
}

function BackLink() {
    return (
        <Link
            to="/customers"
            className="inline-flex min-h-11 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
            <LuChevronLeft aria-hidden className="size-4" /> All customers
        </Link>
    );
}

function VatExemptToggle({ customer }: { customer: Customer }) {
    const save = useCustomerSave(
        "",
        `/api/customers/${customer.id}/vat-exempt`,
    );
    return (
        <div className="space-y-1">
            <Button
                variant="outline"
                className="h-11"
                disabled={save.isPending}
                onClick={() => save.mutate({ vatExempt: !customer.vatExempt })}
            >
                {customer.vatExempt ? "Charge VAT again" : "Mark VAT exempt"}
            </Button>
            {save.isError && (
                <p role="alert" className="text-sm text-late">
                    {save.error.message}
                </p>
            )}
        </div>
    );
}

function SiteRow({
    site,
    canManage,
    editingDistances,
    onEdit,
    onDistances,
    onCloseDistances,
}: {
    site: Site;
    canManage: boolean;
    editingDistances: boolean;
    onEdit: () => void;
    onDistances: () => void;
    onCloseDistances: () => void;
}) {
    const [showDistances, setShowDistances] = useState(false);
    const distances = useSiteDistances(
        site.id,
        showDistances || editingDistances,
    );
    const plants = useActivePlantOptions();
    const hasPoint = site.latitude !== null && site.longitude !== null;

    return (
        <li className="space-y-3 px-4 py-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0 space-y-0.5">
                    <span className="flex flex-wrap items-center gap-2 font-medium">
                        {site.name}
                        {site.status === "Inactive" && (
                            <StatusChip status={site.status} />
                        )}
                    </span>
                    {site.address && (
                        <span className="block text-sm">{site.address}</span>
                    )}
                    {(site.contactName || site.contactPhone) && (
                        <span className="block text-sm text-muted-foreground">
                            {site.contactName}
                            {site.contactName && site.contactPhone && " · "}
                            {site.contactPhone && (
                                <a
                                    href={`tel:${site.contactPhone}`}
                                    className="underline"
                                >
                                    {site.contactPhone}
                                </a>
                            )}
                        </span>
                    )}
                    {site.accessNotes && (
                        <span className="block text-sm text-muted-foreground">
                            {site.accessNotes}
                        </span>
                    )}
                    {hasPoint && (
                        <a
                            href={mapLink(site.latitude!, site.longitude!)}
                            target="_blank"
                            rel="noreferrer"
                            className="inline-flex min-h-11 items-center gap-1 text-sm underline"
                        >
                            <LuMapPin aria-hidden className="size-4" /> Open in
                            maps
                        </a>
                    )}
                </div>
                <div className="flex flex-wrap gap-2">
                    <Button
                        variant="outline"
                        className="h-11"
                        aria-expanded={showDistances}
                        onClick={() => setShowDistances((v) => !v)}
                    >
                        {showDistances ? "Hide distances" : "Distances"}
                    </Button>
                    {canManage && (
                        <Button
                            variant="outline"
                            className="h-11"
                            onClick={onEdit}
                        >
                            Edit
                        </Button>
                    )}
                </div>
            </div>

            {(showDistances || editingDistances) && (
                <div className="space-y-2">
                    {distances.isLoading && (
                        <Skeleton className="h-10 w-full" />
                    )}
                    {distances.isError && (
                        <p role="alert" className="text-sm text-late">
                            {distances.error.message}
                        </p>
                    )}
                    {editingDistances && distances.data && plants.data ? (
                        <DistanceEditor
                            siteId={site.id}
                            plants={plants.data.items}
                            distances={distances.data}
                            onDone={onCloseDistances}
                        />
                    ) : (
                        distances.data && (
                            <div className="flex flex-wrap items-center gap-2">
                                {distances.data.length === 0 ? (
                                    <span className="text-sm text-muted-foreground">
                                        No distances yet. Orders then ask for
                                        the distance each time.
                                    </span>
                                ) : (
                                    distances.data.map((d) => (
                                        <span
                                            key={d.plantId}
                                            className="inline-flex items-center rounded-sm border px-2 py-0.5 text-xs"
                                        >
                                            <span className="font-semibold">
                                                {d.plantCode}
                                            </span>
                                            &nbsp;{d.distanceKm} km
                                        </span>
                                    ))
                                )}
                                {canManage && (
                                    <Button
                                        variant="outline"
                                        className="h-11"
                                        onClick={onDistances}
                                    >
                                        Edit distances
                                    </Button>
                                )}
                            </div>
                        )
                    )}
                </div>
            )}

            {canManage && (
                <DeactivateControl
                    noun="site"
                    status={site.status}
                    usagePath={`/api/sites/${site.id}/usage`}
                    savePath={`/api/sites/${site.id}`}
                    bodyFor={(status) => siteBody(site, { status })}
                />
            )}
        </li>
    );
}
