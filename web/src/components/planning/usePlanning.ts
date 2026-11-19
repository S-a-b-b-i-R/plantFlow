import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Paged } from "@/lib/orders";
import type {
    DriverOption,
    LineToPlan,
    PlantShift,
    PumpOption,
    ShiftPlanDetail,
    ShiftPlanSummary,
} from "@/lib/planning";

export const useShifts = (plantId: string | undefined) =>
    useQuery({
        queryKey: ["planning", "shifts", plantId],
        queryFn: () => api<PlantShift[]>(`/api/plants/${plantId}/shifts`),
        enabled: !!plantId,
    });

// The plans of one plant on one day: at most one per shift.
export const usePlans = (plantId: string | undefined, date: string) =>
    useQuery({
        queryKey: ["planning", "plans", plantId, date],
        queryFn: () =>
            api<Paged<ShiftPlanSummary>>(
                `/api/shift-plans?plantId=${plantId}&from=${date}&to=${date}&pageSize=50`,
            ),
        enabled: !!plantId,
    });

export const usePlan = (id: string) =>
    useQuery({
        queryKey: ["planning", "plan", id],
        queryFn: () => api<ShiftPlanDetail>(`/api/shift-plans/${id}`),
    });

export const useToPlan = (plantId: string | undefined, date: string) =>
    useQuery({
        queryKey: ["planning", "to-plan", plantId, date],
        queryFn: () =>
            api<LineToPlan[]>(
                `/api/shift-plans/to-plan?plantId=${plantId}&date=${date}`,
            ),
        enabled: !!plantId,
    });

export const usePumps = (plantId: string | undefined) =>
    useQuery({
        queryKey: ["planning", "pumps", plantId],
        queryFn: () =>
            api<Paged<PumpOption>>(
                `/api/vehicles?plantId=${plantId}&type=Pump&pageSize=100`,
            ),
        enabled: !!plantId,
    });

export const useDrivers = () =>
    useQuery({
        queryKey: ["drivers", "active"],
        queryFn: () =>
            api<Paged<DriverOption>>("/api/drivers?status=Active&pageSize=100"),
    });

type Call = { url: string; method?: "POST" | "PUT" | "DELETE"; body?: unknown };

// One change to a plan or a pour. Refreshes the plans and the list of lines still to plan.
export function usePlanAct() {
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
            queryClient.invalidateQueries({ queryKey: ["planning"] }),
    });
}
