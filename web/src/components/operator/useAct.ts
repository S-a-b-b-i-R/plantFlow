import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";

// Posts an action, then refreshes everything the console shows. Each card keeps its own error.
export function useAct() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({ url, body }: { url: string; body?: unknown }) =>
            api<unknown>(url, {
                method: "POST",
                body: JSON.stringify(body ?? {}),
            }),
        onSettled: () =>
            Promise.all([
                queryClient.invalidateQueries({ queryKey: ["operator"] }),
                queryClient.invalidateQueries({ queryKey: ["trucks"] }),
                queryClient.invalidateQueries({ queryKey: ["live"] }),
                queryClient.invalidateQueries({ queryKey: ["returns"] }),
            ]),
    });
}
