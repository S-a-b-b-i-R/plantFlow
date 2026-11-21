import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Paged } from "@/lib/orders";
import type {
    ActiveStatus,
    PlantDetail,
    PlantShift,
    PlantSummary,
    PlantUsage,
} from "@/lib/plants";

export type StatusFilter = ActiveStatus | "All";

export const usePlantList = (search: string, status: StatusFilter) =>
    useQuery({
        queryKey: ["plants", "list", search, status],
        queryFn: () => {
            const query = new URLSearchParams({ pageSize: "100" });
            if (search.trim()) query.set("search", search.trim());
            if (status !== "All") query.set("status", status);
            return api<Paged<PlantSummary>>(`/api/plants?${query}`);
        },
    });

export const usePlant = (id: string) =>
    useQuery({
        queryKey: ["plants", "detail", id],
        queryFn: () => api<PlantDetail>(`/api/plants/${id}`),
    });

export const usePlantShifts = (id: string) =>
    useQuery({
        queryKey: ["plants", "shifts", id],
        queryFn: () => api<PlantShift[]>(`/api/plants/${id}/shifts`),
    });

// Fetched only when someone is about to deactivate the plant.
export const usePlantUsage = (id: string, enabled: boolean) =>
    useQuery({
        queryKey: ["plants", "usage", id],
        queryFn: () => api<PlantUsage>(`/api/plants/${id}/usage`),
        enabled,
        staleTime: 0,
    });

// Creates with POST to createPath, or updates with PUT to updatePath when given.
// Every plant query is refreshed afterwards, and so are the plant pickers in other areas.
export function usePlantSave<T = unknown>(
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
            await queryClient.invalidateQueries({ queryKey: ["plants"] });
            await queryClient.invalidateQueries({
                queryKey: ["users", "plants"],
            });
        },
    });
}
