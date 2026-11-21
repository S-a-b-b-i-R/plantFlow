import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Driver, PumpOperator, Vehicle, VehicleType } from "@/lib/fleet";
import type { Paged, PlantOption } from "@/lib/orders";

export const useVehicles = (type: VehicleType, plantId: string) =>
    useQuery({
        queryKey: ["fleet", "vehicles", type, plantId],
        queryFn: () => {
            const query = new URLSearchParams({ type, pageSize: "100" });
            if (plantId) query.set("plantId", plantId);
            return api<Paged<Vehicle>>(`/api/vehicles?${query}`);
        },
    });

export const useDriverList = () =>
    useQuery({
        queryKey: ["fleet", "drivers"],
        queryFn: () => api<Paged<Driver>>("/api/drivers?pageSize=100"),
    });

export const useOperators = () =>
    useQuery({
        queryKey: ["fleet", "operators"],
        queryFn: () =>
            api<Paged<PumpOperator>>("/api/pump-operators?pageSize=100"),
    });

export const useFleetPlants = () =>
    useQuery({
        queryKey: ["fleet", "plants"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });

// POST to createPath, or PUT to updatePath when given. Planning shows pumps and crews, so it is refreshed too.
export function useFleetSave<T = unknown>(
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
            await queryClient.invalidateQueries({ queryKey: ["fleet"] });
            await queryClient.invalidateQueries({ queryKey: ["planning"] });
        },
    });
}
