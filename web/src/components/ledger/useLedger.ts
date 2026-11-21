import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type {
    AvailableCredit,
    LedgerClose,
    LedgerCustomer,
    LedgerStatement,
} from "@/lib/ledger";
import type { Paged } from "@/lib/orders";

// Customers with credits. With all, also those whose credits are used up or closed.
export const useLedgerCustomers = (search: string, all: boolean) =>
    useQuery({
        queryKey: ["ledger", "customers", search, all],
        queryFn: () =>
            api<Paged<LedgerCustomer>>(
                `/api/ledger?pageSize=100&all=${all}&search=${encodeURIComponent(search)}`,
            ),
    });

export const useLedgerStatement = (customerId: string, page: number) =>
    useQuery({
        queryKey: ["ledger", "statement", customerId, page],
        queryFn: () =>
            api<LedgerStatement>(
                `/api/customers/${customerId}/ledger?page=${page}&pageSize=20`,
            ),
        placeholderData: (previous) => previous,
    });

// Closes waiting for a decision. Also feeds the badge in the menu.
export const usePendingCloses = (enabled = true) =>
    useQuery({
        queryKey: ["ledger", "closes", "Pending"],
        queryFn: () => api<LedgerClose[]>("/api/ledger/closes?status=Pending"),
        enabled,
        refetchInterval: 60_000,
    });

export const useAvailableCredits = (orderId: string, enabled: boolean) =>
    useQuery({
        queryKey: ["ledger", "available", orderId],
        queryFn: () =>
            api<AvailableCredit[]>(`/api/orders/${orderId}/ledger-credits`),
        enabled,
    });

type Call = { url: string; body?: unknown };

// Any change to the ledger, then refreshes everything ledger and invoice.
export function useLedgerAct() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({ url, body }: Call) =>
            api<unknown>(url, {
                method: "POST",
                body: JSON.stringify(body ?? {}),
            }),
        onSettled: () =>
            Promise.all([
                queryClient.invalidateQueries({ queryKey: ["ledger"] }),
                queryClient.invalidateQueries({ queryKey: ["invoice"] }),
            ]),
    });
}
