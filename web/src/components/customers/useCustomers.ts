import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StatusFilter } from "@/components/plants/usePlants";
import { api } from "@/lib/api";
import type {
    Customer,
    LinkableUser,
    OpenOrders,
    SalesRep,
    Site,
    SiteDistance,
} from "@/lib/customers";
import type { Paged, PlantOption } from "@/lib/orders";

export const useCustomerList = (search: string, status: StatusFilter) =>
    useQuery({
        queryKey: ["customers", "list", search, status],
        queryFn: () => {
            const query = new URLSearchParams({ pageSize: "50" });
            if (search.trim()) query.set("search", search.trim());
            if (status !== "All") query.set("status", status);
            return api<Paged<Customer>>(`/api/customers?${query}`);
        },
    });

export const useCustomer = (id: string) =>
    useQuery({
        queryKey: ["customers", "detail", id],
        queryFn: () => api<Customer>(`/api/customers/${id}`),
    });

export const useSites = (customerId: string) =>
    useQuery({
        queryKey: ["customers", "sites", customerId],
        queryFn: () =>
            api<Paged<Site>>(`/api/customers/${customerId}/sites?pageSize=100`),
    });

export const useSiteDistances = (siteId: string, enabled: boolean) =>
    useQuery({
        queryKey: ["customers", "distances", siteId],
        queryFn: () => api<SiteDistance[]>(`/api/sites/${siteId}/distances`),
        enabled,
    });

// Fetched only when someone is about to deactivate a customer or a site.
export const useOpenOrders = (path: string, enabled: boolean) =>
    useQuery({
        queryKey: ["customers", "usage", path],
        queryFn: () => api<OpenOrders>(path),
        enabled,
        staleTime: 0,
    });

export const useActivePlantOptions = () =>
    useQuery({
        queryKey: ["customers", "plants"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });

export const useSalesReps = (status: StatusFilter = "All") =>
    useQuery({
        queryKey: ["salesreps", "list", status],
        queryFn: () =>
            api<Paged<SalesRep>>(
                `/api/sales-reps?pageSize=100${status === "All" ? "" : `&status=${status}`}`,
            ),
    });

// The signed-in user's own sales rep, or null when none is linked.
export const useMyRep = () =>
    useQuery({
        queryKey: ["salesreps", "mine"],
        // React Query refuses undefined data, so "no rep" (204) becomes null.
        queryFn: async () =>
            (await api<SalesRep | undefined>("/api/sales-reps/mine")) ?? null,
    });

export const useLinkableUsers = (enabled: boolean) =>
    useQuery({
        queryKey: ["salesreps", "users"],
        queryFn: () => api<LinkableUser[]>("/api/sales-reps/linkable-users"),
        enabled,
    });

// POST to createPath, or PUT to updatePath when given. The customer and sales rep areas are both refreshed,
// since customers show their rep's name.
export function useCustomerSave<T = unknown>(
    createPath: string,
    updatePath: string | null,
) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (body: unknown) =>
            api<T>(updatePath ?? createPath, {
                method: updatePath ? "PUT" : "POST",
                body: JSON.stringify(body),
            }),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: ["customers"] });
            await queryClient.invalidateQueries({ queryKey: ["salesreps"] });
        },
    });
}
