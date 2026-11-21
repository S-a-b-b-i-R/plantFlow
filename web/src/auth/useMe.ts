import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { Me } from "@/lib/types";
import { useAuth } from "./auth-context";

// The signed-in person as the API sees them: status, company and permissions.
// While they are waiting for approval it keeps checking, so approval shows up without a reload.
export function useMe() {
    const { firebaseUser } = useAuth();

    return useQuery({
        queryKey: ["me", firebaseUser?.uid],
        queryFn: () => api<Me>("/api/me"),
        enabled: !!firebaseUser,
        retry: false,
        // Keep checking while someone waits for approval or for their company to be reactivated.
        refetchInterval: (query) =>
            query.state.data?.status === "Pending"
                ? 15_000
                : query.state.data?.companySuspended
                  ? 60_000
                  : false,
    });
}
