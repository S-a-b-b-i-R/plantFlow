import {
    useMutation,
    useQueries,
    useQuery,
    useQueryClient,
} from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Paged, PlantOption, ProductOption } from "@/lib/orders";
import type { CustomerPick, MaterialOption } from "@/lib/pricing";
import type { PriceBookKind } from "./kind";

type Call = { url: string; method?: "POST" | "PUT" | "DELETE"; body?: unknown };

// One change to a price book row (submit, approve, send back, end, delete). Refreshes every price book list.
export function useBookAct() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({ url, method = "POST", body }: Call) =>
            api<unknown>(url, {
                method,
                body:
                    method === "DELETE"
                        ? undefined
                        : JSON.stringify(body ?? {}),
            }),
        onSettled: () =>
            queryClient.invalidateQueries({ queryKey: ["pricebook"] }),
    });
}

// Saves the form of a new row (POST) or an existing draft (PUT).
export function useBookSave(endpoint: string, id: string | null) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (body: unknown) =>
            api<unknown>(id ? `${endpoint}/${id}` : endpoint, {
                method: id ? "PUT" : "POST",
                body: JSON.stringify(body),
            }),
        onSuccess: () =>
            queryClient.invalidateQueries({ queryKey: ["pricebook"] }),
    });
}

// How many rows are waiting for approval across these kinds. Drives the Approvals tab badge and the inbox.
export function usePendingTotal(kinds: PriceBookKind[]) {
    return useQueries({
        queries: kinds.map((kind) => ({
            queryKey: ["pricebook", kind.key, "pending-count"],
            queryFn: () =>
                api<Paged<unknown>>(
                    `${kind.endpoint}?status=${kind.statuses.pending}&pageSize=1`,
                ),
        })),
        combine: (results) =>
            results.reduce((sum, r) => sum + (r.data?.totalCount ?? 0), 0),
    });
}

export const usePlants = () =>
    useQuery({
        queryKey: ["plants", "active"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });

export const useProducts = () =>
    useQuery({
        queryKey: ["products", "active"],
        queryFn: () =>
            api<Paged<ProductOption>>(
                "/api/products?status=Active&pageSize=100",
            ),
    });

export const useMaterials = () =>
    useQuery({
        queryKey: ["materials", "active"],
        queryFn: () =>
            api<Paged<MaterialOption>>(
                "/api/materials?status=Active&pageSize=100",
            ),
    });

export const useCustomerSearch = (search: string, enabled: boolean) =>
    useQuery({
        queryKey: ["customers", "pick", search],
        queryFn: () =>
            api<Paged<CustomerPick>>(
                `/api/customers?status=Active&pageSize=8&search=${encodeURIComponent(search)}`,
            ),
        enabled,
    });
