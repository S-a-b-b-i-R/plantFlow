import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";

type SiteDistance = { plantId: string; plantCode: string; distanceKm: number };

// The distance kept for a site and plant, if there is one. The order falls back to it when none is typed in.
export function useStoredDistance(siteId: string, plantId: string) {
    const distances = useQuery({
        queryKey: ["distances", siteId],
        queryFn: () => api<SiteDistance[]>(`/api/sites/${siteId}/distances`),
        enabled: !!siteId,
    });
    return {
        loading: !!siteId && distances.isPending,
        km:
            distances.data?.find((d) => d.plantId === plantId)?.distanceKm ??
            null,
    };
}
