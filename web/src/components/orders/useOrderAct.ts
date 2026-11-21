import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";

type Call = { url: string; method?: "POST" | "PUT" | "DELETE"; body?: unknown };

// Sends one change to an order, then refreshes the order, the list and the invoice.
export function useOrderAct() {
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
            Promise.all([
                queryClient.invalidateQueries({ queryKey: ["order"] }),
                queryClient.invalidateQueries({ queryKey: ["orders"] }),
                queryClient.invalidateQueries({ queryKey: ["invoice"] }),
                // Closing, cancelling or using credit changes the customer's ledger.
                queryClient.invalidateQueries({ queryKey: ["ledger"] }),
            ]),
    });
}
