import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { CompanySummary, UnassignedUser } from "@/lib/platform";

export const useCompanies = () =>
    useQuery({
        queryKey: ["platform", "companies"],
        queryFn: () => api<CompanySummary[]>("/api/platform/tenants"),
    });

// People who signed in without entering a company's code. Platform admins themselves are left out.
export const useUnassigned = () =>
    useQuery({
        queryKey: ["platform", "unassigned"],
        queryFn: () => api<UnassignedUser[]>("/api/platform/users/unassigned"),
        select: (users) => users.filter((u) => !u.isPlatformAdmin),
        refetchInterval: 60_000,
    });

function usePlatformMutation<T, R>(send: (input: T) => Promise<R>) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: send,
        onSuccess: () =>
            queryClient.invalidateQueries({ queryKey: ["platform"] }),
    });
}

export const useCreateCompany = () =>
    usePlatformMutation((input: { name: string; slug: string }) =>
        api<CompanySummary>("/api/platform/tenants", {
            method: "POST",
            body: JSON.stringify(input),
        }),
    );

export const useCompanyStatus = (companyId: string) =>
    usePlatformMutation(
        ({
            action,
            reason,
        }: {
            action: "suspend" | "reactivate";
            reason?: string;
        }) =>
            api<void>(`/api/platform/tenants/${companyId}/${action}`, {
                method: "POST",
                body: JSON.stringify({ reason }),
            }),
    );

export const useAssignUser = (userId: string) =>
    usePlatformMutation((input: { tenantId: string; asAdmin: boolean }) =>
        api<void>(`/api/platform/users/${userId}/assign`, {
            method: "POST",
            body: JSON.stringify(input),
        }),
    );
