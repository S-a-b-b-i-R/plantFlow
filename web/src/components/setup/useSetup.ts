import { useQuery } from "@tanstack/react-query";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { api } from "@/lib/api";
import type { SetupProgress } from "@/lib/setup";

// The company Admin's getting-started progress. Re-read whenever a screen using it opens,
// so steps done elsewhere tick off without a reload.
export const useSetup = () => {
    const me = useMe().data;
    return useQuery({
        queryKey: ["setup"],
        queryFn: () => api<SetupProgress>("/api/setup"),
        enabled: !!me && can(me, PERMISSIONS.settingsManage),
    });
};
