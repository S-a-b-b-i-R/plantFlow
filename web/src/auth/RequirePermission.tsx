import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useMe } from "@/auth/useMe";
import { canOpen, navigation } from "@/layout/navigation";

// Wraps a screen so it only opens for someone allowed to use it. The API refuses
// the data anyway; this keeps people off screens that would only show errors.
export function RequirePermission() {
    const me = useMe().data!;
    const { pathname } = useLocation();
    const item = navigation.find(
        (entry) => pathname === entry.to || pathname.startsWith(`${entry.to}/`),
    );

    if (item && !canOpen(me, item)) {
        return <Navigate to="/" replace />;
    }
    return <Outlet />;
}
