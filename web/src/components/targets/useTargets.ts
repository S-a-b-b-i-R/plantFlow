import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { TargetLevel, TargetQuarter } from "@/lib/targets";

// Without a quarter, the one running today.
export const useTargetQuarter = (
    at: { year: number; quarter: number } | null,
) =>
    useQuery({
        queryKey: ["targets", at?.year ?? "now", at?.quarter ?? "now"],
        queryFn: () =>
            api<TargetQuarter>(
                at
                    ? `/api/targets?year=${at.year}&quarter=${at.quarter}`
                    : "/api/targets",
            ),
        placeholderData: (previous) => previous,
    });

export type SetTarget = {
    year: number;
    quarter: number;
    level: TargetLevel;
    plantId: string | null;
    salesRepId: string | null;
    volumeCft: number;
};

export function useSetTarget() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (body: SetTarget) =>
            api<TargetQuarter>("/api/targets", {
                method: "PUT",
                body: JSON.stringify(body),
            }),
        onSettled: () =>
            queryClient.invalidateQueries({ queryKey: ["targets"] }),
    });
}

export function useRemoveTarget() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (id: string) =>
            api<unknown>(`/api/targets/${id}`, { method: "DELETE" }),
        onSettled: () =>
            queryClient.invalidateQueries({ queryKey: ["targets"] }),
    });
}
