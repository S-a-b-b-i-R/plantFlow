import { NavLink, Outlet } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/auth/auth-context";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";

// What appears in the navigation, and the permission that shows it.
const navigation = [
    {
        to: "/live",
        label: "Live operations",
        permission: PERMISSIONS.dashboardsView,
    },
];

export function AppShell() {
    const { signOut } = useAuth();
    const me = useMe().data!;

    return (
        <div className="min-h-screen bg-muted/30">
            <header className="border-b bg-background">
                <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
                    <div className="flex items-baseline gap-2">
                        <span className="text-lg font-semibold tracking-tight">
                            PlantFlow
                        </span>
                        <span className="text-sm text-muted-foreground">
                            {me.tenantName}
                        </span>
                    </div>

                    <nav className="flex flex-1 gap-1">
                        {navigation
                            .filter((item) => can(me, item.permission))
                            .map((item) => (
                                <NavLink
                                    key={item.to}
                                    to={item.to}
                                    className={({ isActive }) =>
                                        `rounded-md px-3 py-1.5 text-sm font-medium ${
                                            isActive
                                                ? "bg-primary text-primary-foreground"
                                                : "text-muted-foreground hover:bg-muted"
                                        }`
                                    }
                                >
                                    {item.label}
                                </NavLink>
                            ))}
                    </nav>

                    <div className="flex items-center gap-3 text-sm">
                        <span className="hidden text-muted-foreground sm:inline">
                            {me.displayName ?? me.email}
                        </span>
                        <Button
                            variant="outline"
                            size="sm"
                            onClick={() => void signOut()}
                        >
                            Sign out
                        </Button>
                    </div>
                </div>
            </header>

            <main className="mx-auto max-w-7xl px-4 py-6">
                <Outlet />
            </main>
        </div>
    );
}
