import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Paged, PlantOption } from "@/lib/orders";
import type {
    PermissionOverride,
    RoleOption,
    UserDetail,
    UserSummary,
} from "@/lib/users";

export const useUsers = () =>
    useQuery({
        queryKey: ["users", "list"],
        queryFn: () => api<UserSummary[]>("/api/users"),
    });

export const useUserDetail = (userId: string) =>
    useQuery({
        queryKey: ["users", "detail", userId],
        queryFn: () => api<UserDetail>(`/api/users/${userId}`),
    });

export const useRoles = () =>
    useQuery({
        queryKey: ["users", "roles"],
        queryFn: () => api<RoleOption[]>("/api/roles"),
        staleTime: 5 * 60_000,
    });

export const usePermissionKeys = () =>
    useQuery({
        queryKey: ["users", "permission-keys"],
        queryFn: () => api<string[]>("/api/permissions"),
        staleTime: Infinity,
    });

// For the badge on Users in the menu.
export const usePendingCount = () =>
    useQuery({
        queryKey: ["users", "pending-count"],
        queryFn: () => api<{ count: number }>("/api/users/pending-count"),
        refetchInterval: 60_000,
    });

export const useActivePlants = () =>
    useQuery({
        queryKey: ["users", "plants"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });

// Every change to a user goes through here, so the list, the open detail and the badge all refresh.
function useUserMutation<T>(send: (input: T) => Promise<unknown>) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: send,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ["users"] }),
    });
}

export type StatusAction = "approve" | "reject" | "enable" | "disable";

export const useStatusAction = (userId: string) =>
    useUserMutation(
        ({ action, reason }: { action: StatusAction; reason?: string }) =>
            api<void>(`/api/users/${userId}/${action}`, {
                method: "POST",
                body:
                    reason === undefined
                        ? undefined
                        : JSON.stringify({ reason }),
            }),
    );

export const useSetRoles = (userId: string) =>
    useUserMutation((roleIds: string[]) =>
        api<void>(`/api/users/${userId}/roles`, {
            method: "PUT",
            body: JSON.stringify({ roleIds }),
        }),
    );

export const useSetOverrides = (userId: string) =>
    useUserMutation((overrides: PermissionOverride[]) =>
        api<void>(`/api/users/${userId}/permissions`, {
            method: "PUT",
            body: JSON.stringify({ overrides }),
        }),
    );

export const useSetUserPlants = (userId: string) =>
    useUserMutation((plantIds: string[]) =>
        api<void>(`/api/users/${userId}/plants`, {
            method: "PUT",
            body: JSON.stringify({ plantIds }),
        }),
    );

export interface JoinCode {
    code: string;
    renewedAtUtc: string | null;
    method: "Code";
}

// The code people enter to ask to join this company.
export const useJoinCode = () =>
    useQuery({
        queryKey: ["users", "join-code"],
        queryFn: () => api<JoinCode>("/api/join-code"),
    });

export const useRenewJoinCode = () =>
    useUserMutation(() =>
        api<JoinCode>("/api/join-code/renew", { method: "POST" }),
    );
