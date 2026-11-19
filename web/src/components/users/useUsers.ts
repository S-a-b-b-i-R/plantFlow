import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Paged, PlantOption } from "@/lib/orders";
import type { UserSummary } from "@/lib/users";

export const useUsers = () =>
    useQuery({
        queryKey: ["users", "list"],
        queryFn: () => api<UserSummary[]>("/api/users"),
    });

export const useActivePlants = () =>
    useQuery({
        queryKey: ["users", "plants"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });

export function useSetUserPlants() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({
            userId,
            plantIds,
        }: {
            userId: string;
            plantIds: string[];
        }) =>
            api<void>(`/api/users/${userId}/plants`, {
                method: "PUT",
                body: JSON.stringify({ plantIds }),
            }),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ["users"] }),
    });
}
