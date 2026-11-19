import { useDeferredValue, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { LuChevronRight, LuSearch } from "react-icons/lu";
import {
    useCustomer,
    useCustomerSearch,
    useSites,
} from "@/components/surveys/useSurveys";
import { inputClass, panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

// Find a site: pick the customer, then one of their sites.
export function Surveys() {
    const [params, setParams] = useSearchParams();
    const customerId = params.get("customer");
    const [search, setSearch] = useState("");
    const deferred = useDeferredValue(search.trim());

    const customer = useCustomer(customerId);
    const customers = useCustomerSearch(deferred, !customerId);
    const sites = useSites(customerId);

    return (
        <div className="mx-auto max-w-2xl space-y-4">
            <header>
                <h1 className="display text-4xl leading-none">Site surveys</h1>
                <p className="mt-2 text-sm text-muted-foreground">
                    Find the site, then record what you see there: where it is,
                    how to reach it and where the pump goes.
                </p>
            </header>

            {customerId ? (
                <>
                    <div className="flex flex-wrap items-center justify-between gap-3 rounded-sm border bg-muted/40 px-4 py-3">
                        <span className="font-semibold">
                            {customer.data?.name ?? "…"}
                        </span>
                        <button
                            type="button"
                            onClick={() => setParams({}, { replace: true })}
                            className="h-10 rounded-sm border bg-card px-3 text-sm hover:border-ink"
                        >
                            Change customer
                        </button>
                    </div>

                    {sites.isLoading && <Skeleton className="h-24 w-full" />}
                    {sites.isError && (
                        <p role="alert" className="text-sm text-late">
                            {sites.error.message}
                        </p>
                    )}
                    {sites.data && sites.data.items.length === 0 && (
                        <p className="text-sm text-muted-foreground">
                            This customer has no active sites. Add one under
                            master data first.
                        </p>
                    )}
                    <ul className={cn(panelClass, "divide-y")}>
                        {sites.data?.items.map((site) => (
                            <li key={site.id}>
                                <Link
                                    to={`/surveys/${site.id}`}
                                    className="flex min-h-14 items-center justify-between gap-3 px-4 py-3 hover:bg-muted/50"
                                >
                                    <span>
                                        <span className="block font-medium">
                                            {site.name}
                                        </span>
                                        {site.address && (
                                            <span className="block text-sm text-muted-foreground">
                                                {site.address}
                                            </span>
                                        )}
                                    </span>
                                    <LuChevronRight
                                        aria-hidden
                                        className="size-5 shrink-0 text-muted-foreground"
                                    />
                                </Link>
                            </li>
                        ))}
                    </ul>
                </>
            ) : (
                <div className="space-y-2">
                    <span className="relative block">
                        <LuSearch
                            aria-hidden
                            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                        />
                        <input
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            placeholder="Search the customer's name, code or phone"
                            aria-label="Search customers"
                            className={cn(inputClass, "h-12 pl-9")}
                        />
                    </span>
                    {customers.isError ? (
                        <p role="alert" className="text-sm text-late">
                            {customers.error.message}
                        </p>
                    ) : (
                        <ul className={cn(panelClass, "divide-y")}>
                            {(customers.data?.items ?? []).map((c) => (
                                <li key={c.id}>
                                    <button
                                        type="button"
                                        onClick={() =>
                                            setParams(
                                                { customer: c.id },
                                                { replace: true },
                                            )
                                        }
                                        className="flex min-h-14 w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-muted/50"
                                    >
                                        <span className="font-medium">
                                            {c.name}
                                        </span>
                                        <span className="text-sm text-muted-foreground">
                                            {c.code}
                                        </span>
                                    </button>
                                </li>
                            ))}
                            {customers.data?.items.length === 0 && (
                                <li className="px-4 py-3 text-sm text-muted-foreground">
                                    No customer matches.
                                </li>
                            )}
                        </ul>
                    )}
                </div>
            )}
        </div>
    );
}
