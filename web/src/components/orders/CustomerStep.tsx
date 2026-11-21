import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { LuCheck, LuSearch } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { Field } from "./Field";
import { SurveyChoice } from "./SurveyChoice";
import { inputClass, textareaClass } from "./styles";
import { useStoredDistance } from "./useStoredDistance";
import type { Draft } from "./orderDraft";
import { api } from "@/lib/api";
import type {
    CustomerOption,
    Paged,
    PlantOption,
    SalesRepOption,
    SiteOption,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

export function CustomerStep({
    draft,
    change,
    locked,
}: {
    draft: Draft;
    change: (patch: Partial<Draft>) => void;
    // Revising or amending keeps the customer fixed: it is a different order otherwise.
    locked: boolean;
}) {
    const [search, setSearch] = useState("");
    const me = useMe().data!;
    // A sales rep's orders are always their own, so they don't pick a rep.
    const isRep =
        can(me, PERMISSIONS.customersManage) &&
        !can(me, PERMISSIONS.customersAssign);

    const customers = useQuery({
        queryKey: ["customers", "pick", search.trim()],
        queryFn: () =>
            api<Paged<CustomerOption>>(
                `/api/customers?status=Active&pageSize=8&search=${encodeURIComponent(search.trim())}`,
            ),
        enabled: !draft.customerId,
    });
    const sites = useQuery({
        queryKey: ["sites", draft.customerId],
        queryFn: () =>
            api<Paged<SiteOption>>(
                `/api/customers/${draft.customerId}/sites?pageSize=100`,
            ),
        enabled: !!draft.customerId,
    });
    const plants = useQuery({
        queryKey: ["plants", "active"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });
    const reps = useQuery({
        queryKey: ["reps", "active"],
        queryFn: () =>
            api<Paged<SalesRepOption>>(
                "/api/sales-reps?status=Active&pageSize=100",
            ),
        enabled: !isRep,
    });
    const stored = useStoredDistance(draft.siteId, draft.plantId);

    const pickCustomer = (c: CustomerOption) =>
        change({
            customerId: c.id,
            customerName: c.name,
            salesRepId: isRep ? draft.salesRepId : (c.salesRepId ?? ""),
            siteId: "",
            surveyId: "",
            requestSurvey: false,
        });

    const usableSites = (sites.data?.items ?? []).filter(
        (s) => s.status === "Active" || s.id === draft.siteId,
    );

    return (
        <div className="space-y-8">
            <section className="space-y-3">
                <h2 className="display text-2xl">Who is it for?</h2>

                {draft.customerId ? (
                    <div className="flex flex-wrap items-center justify-between gap-3 rounded-sm border bg-muted/40 px-4 py-3">
                        <span className="font-semibold">
                            {draft.customerName}
                        </span>
                        {!locked && (
                            <button
                                type="button"
                                onClick={() =>
                                    change({
                                        customerId: "",
                                        customerName: "",
                                        siteId: "",
                                        surveyId: "",
                                        requestSurvey: false,
                                    })
                                }
                                className="h-9 rounded-sm border bg-card px-3 text-sm hover:border-ink"
                            >
                                Change customer
                            </button>
                        )}
                    </div>
                ) : (
                    <div className="space-y-2">
                        <span className="relative block">
                            <LuSearch
                                aria-hidden
                                className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                            />
                            <input
                                autoFocus
                                value={search}
                                onChange={(e) => setSearch(e.target.value)}
                                placeholder="Search the customer's name, code or phone"
                                aria-label="Search customers"
                                className={cn(inputClass, "pl-9")}
                            />
                        </span>
                        {customers.isError ? (
                            <p className="text-sm text-late" role="alert">
                                {customers.error.message}
                            </p>
                        ) : (
                            <ul className="divide-y rounded-sm border bg-card">
                                {(customers.data?.items ?? []).map((c) => (
                                    <li key={c.id}>
                                        <button
                                            type="button"
                                            onClick={() => pickCustomer(c)}
                                            className="flex w-full items-baseline justify-between gap-3 px-4 py-3 text-left hover:bg-muted/50"
                                        >
                                            <span className="font-medium">
                                                {c.name}
                                            </span>
                                            <span className="text-xs text-muted-foreground">
                                                {c.code}
                                            </span>
                                        </button>
                                    </li>
                                ))}
                                {customers.data?.items.length === 0 && (
                                    <li className="px-4 py-6 text-center text-sm text-muted-foreground">
                                        No active customer matches that.
                                    </li>
                                )}
                            </ul>
                        )}
                    </div>
                )}
            </section>

            {draft.customerId && (
                <>
                    <section className="space-y-3">
                        <h2 className="display text-2xl">Which site?</h2>
                        {sites.isPending ? (
                            <p className="text-sm text-muted-foreground">
                                Loading the sites…
                            </p>
                        ) : usableSites.length === 0 ? (
                            <p className="text-sm text-muted-foreground">
                                This customer has no active site. Add one under
                                the customer first.
                            </p>
                        ) : (
                            <div
                                className="grid gap-2 sm:grid-cols-2"
                                role="radiogroup"
                                aria-label="Site"
                            >
                                {usableSites.map((s) => (
                                    <ChoiceCard
                                        key={s.id}
                                        selected={draft.siteId === s.id}
                                        onSelect={() =>
                                            change(
                                                s.id === draft.siteId
                                                    ? { siteId: s.id }
                                                    : {
                                                          siteId: s.id,
                                                          surveyId: "",
                                                          requestSurvey: false,
                                                      },
                                            )
                                        }
                                        title={s.name}
                                        detail={s.address}
                                    />
                                ))}
                            </div>
                        )}
                    </section>

                    <section className="space-y-3">
                        <h2 className="display text-2xl">
                            Where is it made, and when?
                        </h2>
                        <div
                            className="grid gap-2 sm:grid-cols-3"
                            role="radiogroup"
                            aria-label="Plant"
                        >
                            {(plants.data?.items ?? []).map((p) => (
                                <ChoiceCard
                                    key={p.id}
                                    selected={draft.plantId === p.id}
                                    onSelect={() => change({ plantId: p.id })}
                                    title={p.name}
                                    detail={p.code}
                                />
                            ))}
                        </div>
                        <div className="grid gap-3 sm:grid-cols-2">
                            <Field label="Date the concrete is needed">
                                <input
                                    type="date"
                                    value={draft.date}
                                    onChange={(e) =>
                                        change({ date: e.target.value })
                                    }
                                    className={inputClass}
                                />
                            </Field>
                            <Field
                                label="Distance from plant to site, km"
                                hint={
                                    !draft.siteId || !draft.plantId
                                        ? "Choose the site and the plant first."
                                        : stored.loading
                                          ? "Looking up the stored distance…"
                                          : stored.km !== null
                                            ? `Leave empty to use the stored ${stored.km} km.`
                                            : "No distance is stored for this site and plant, so enter it."
                                }
                            >
                                <input
                                    inputMode="decimal"
                                    value={draft.distance}
                                    onChange={(e) =>
                                        change({ distance: e.target.value })
                                    }
                                    placeholder={
                                        stored.km !== null
                                            ? String(stored.km)
                                            : "For example 12.5"
                                    }
                                    className={inputClass}
                                />
                            </Field>
                        </div>
                    </section>

                    <section className="space-y-3">
                        <h2 className="display text-2xl">A few details</h2>
                        <div className="grid gap-3 sm:grid-cols-2">
                            {!isRep && (
                                <Field label="Sales rep">
                                    <select
                                        value={draft.salesRepId}
                                        onChange={(e) =>
                                            change({
                                                salesRepId: e.target.value,
                                            })
                                        }
                                        className={inputClass}
                                    >
                                        <option value="">
                                            The customer's own rep
                                        </option>
                                        {(reps.data?.items ?? []).map((r) => (
                                            <option key={r.id} value={r.id}>
                                                {r.name}
                                            </option>
                                        ))}
                                    </select>
                                </Field>
                            )}
                            <Field label="Payment terms">
                                <input
                                    value={draft.paymentTerms}
                                    onChange={(e) =>
                                        change({ paymentTerms: e.target.value })
                                    }
                                    placeholder="For example 30 days"
                                    maxLength={100}
                                    className={inputClass}
                                />
                            </Field>
                            {draft.siteId && (
                                <Field
                                    label="Site survey"
                                    hint="Pump jobs need a survey before they can be planned. Pick one of the site's last three, or ask a surveyor for a new one."
                                    className="sm:col-span-2"
                                >
                                    <SurveyChoice
                                        siteId={draft.siteId}
                                        value={{
                                            surveyId: draft.surveyId,
                                            requestSurvey: draft.requestSurvey,
                                        }}
                                        onChange={change}
                                    />
                                </Field>
                            )}
                            <Field label="Notes" className="sm:col-span-2">
                                <textarea
                                    value={draft.notes}
                                    onChange={(e) =>
                                        change({ notes: e.target.value })
                                    }
                                    rows={2}
                                    maxLength={1000}
                                    className={textareaClass}
                                />
                            </Field>
                        </div>
                    </section>
                </>
            )}
        </div>
    );
}

function ChoiceCard({
    selected,
    onSelect,
    title,
    detail,
}: {
    selected: boolean;
    onSelect: () => void;
    title: string;
    detail?: string | null;
}) {
    return (
        <button
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={onSelect}
            className={cn(
                "flex items-start justify-between gap-3 rounded-sm border px-4 py-3 text-left",
                selected
                    ? "border-ink bg-ink/5 ring-1 ring-ink"
                    : "bg-card hover:border-ink",
            )}
        >
            <span className="min-w-0">
                <span className="block font-medium">{title}</span>
                {detail && (
                    <span className="block truncate text-xs text-muted-foreground">
                        {detail}
                    </span>
                )}
            </span>
            {selected && (
                <LuCheck aria-hidden className="mt-0.5 size-4 shrink-0" />
            )}
        </button>
    );
}
