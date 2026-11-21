import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Material, Product, ServiceCharge } from "@/lib/catalogue";
import type { Paged } from "@/lib/orders";

export const useProducts = () =>
    useQuery({
        queryKey: ["catalogue", "products"],
        queryFn: () => api<Paged<Product>>("/api/products?pageSize=100"),
    });

export const useMaterials = () =>
    useQuery({
        queryKey: ["catalogue", "materials"],
        queryFn: () => api<Paged<Material>>("/api/materials?pageSize=100"),
    });

export const useCharges = () =>
    useQuery({
        queryKey: ["catalogue", "charges"],
        queryFn: () =>
            api<Paged<ServiceCharge>>("/api/service-charges?pageSize=100"),
    });

// Fetched only when someone is about to deactivate a product or material.
export const useUsage = <T>(path: string, enabled: boolean) =>
    useQuery({
        queryKey: ["catalogue", "usage", path],
        queryFn: () => api<T>(path),
        enabled,
        staleTime: 0,
    });

// POST to createPath, or PUT to updatePath when given. The order and price screens keep their own
// product and material lists, so those are refreshed too.
export function useCatalogueSave(
    createPath: string,
    updatePath: string | null,
) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (body: unknown) =>
            api<unknown>(updatePath ?? createPath, {
                method: updatePath ? "PUT" : "POST",
                body: JSON.stringify(body),
            }),
        onSuccess: async () => {
            await queryClient.invalidateQueries({ queryKey: ["catalogue"] });
            await queryClient.invalidateQueries({ queryKey: ["products"] });
            await queryClient.invalidateQueries({ queryKey: ["materials"] });
        },
    });
}
