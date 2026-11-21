import { Navigate } from "react-router-dom";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { useSetup } from "@/components/setup/useSetup";
import { firstAllowed } from "@/layout/navigation";

export function Home() {
    const me = useMe().data!;
    const setup = useSetup();
    // A company Admin starts on Get started until the company is set up.
    if (can(me, PERMISSIONS.settingsManage)) {
        if (setup.isLoading) return null;
        if (setup.data && setup.data.done < setup.data.total) {
            return <Navigate to="/setup" replace />;
        }
    }
    const target = firstAllowed(me);

    if (target) return <Navigate to={target} replace />;

    return (
        <div className="mx-auto max-w-md py-16 text-center">
            <h1 className="display text-3xl">Nothing here for your role yet</h1>
            <p className="mt-3 text-sm text-muted-foreground">
                Your account is active, but none of the screens built so far
                are open to your role. Ask your company admin if you need
                access to the live board or the batching console.
            </p>
        </div>
    );
}
