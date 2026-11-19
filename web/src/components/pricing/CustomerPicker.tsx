import { useState } from "react";
import { inputClass } from "@/components/orders/styles";
import { useCustomerSearch } from "./useBook";

// Picks one customer by typing two letters of the name or code. Empty means no customer.
export function CustomerPicker({
    label,
    onPick,
    placeholder,
}: {
    // The picked customer's code, or "" when none is picked.
    label: string;
    onPick: (customer: { id: string; code: string } | null) => void;
    placeholder: string;
}) {
    const [search, setSearch] = useState("");
    const customers = useCustomerSearch(
        search.trim(),
        !label && search.trim().length >= 2,
    );

    if (label) {
        return (
            <span className="flex h-10 items-center justify-between gap-2 rounded-sm border bg-muted/40 px-3 text-sm">
                <span className="font-medium">{label}</span>
                <button
                    type="button"
                    className="text-muted-foreground underline"
                    onClick={() => onPick(null)}
                >
                    Change
                </button>
            </span>
        );
    }

    return (
        <>
            <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={placeholder}
                className={inputClass}
            />
            {customers.data && customers.data.items.length > 0 && (
                <ul className="mt-1 divide-y rounded-sm border bg-card text-sm">
                    {customers.data.items.map((c) => (
                        <li key={c.id}>
                            <button
                                type="button"
                                className="w-full px-3 py-2 text-left hover:bg-muted/40"
                                onClick={() => {
                                    onPick({ id: c.id, code: c.code });
                                    setSearch("");
                                }}
                            >
                                <span className="font-medium">{c.code}</span>{" "}
                                {c.name}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
        </>
    );
}
