import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";

export interface RoleDraft {
    name: string;
    permissions: string[];
}

// Roles live under the "users" key: the users list shows role names, so both refresh together.
function useRoleMutation<T, R>(send: (input: T) => Promise<R>) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: send,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ["users"] }),
    });
}

export const useCreateRole = () =>
    useRoleMutation((draft: RoleDraft) =>
        api<{ id: string }>("/api/roles", {
            method: "POST",
            body: JSON.stringify(draft),
        }),
    );

export const useUpdateRole = (roleId: string) =>
    useRoleMutation((draft: RoleDraft) =>
        api<void>(`/api/roles/${roleId}`, {
            method: "PUT",
            body: JSON.stringify(draft),
        }),
    );

export const useDeleteRole = (roleId: string) =>
    useRoleMutation(() =>
        api<void>(`/api/roles/${roleId}`, { method: "DELETE" }),
    );
