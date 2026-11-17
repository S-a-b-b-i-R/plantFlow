import { NavLink, Outlet } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Mark } from "@/components/Mark";
import { useAuth } from "@/auth/auth-context";
import { canOpen, navigation } from "@/layout/navigation";
import { useMe } from "@/auth/useMe";
import { LuLogOut } from "react-icons/lu";

export function AppShell() {
    const { signOut } = useAuth();
    const me = useMe().data!;

    const initials = (me.displayName ?? me.email)
        .split(/[\s@.]+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0].toUpperCase())
        .join("");

    return (
        <div className="min-h-screen bg-background">
            <header className="bg-chrome text-chrome-foreground">
                <div className="mx-auto flex max-w-352 flex-wrap items-center gap-x-6 px-4 sm:px-5">
                    <div className="flex items-center gap-2.5 py-3">
                        <Mark className="size-7" />
                        <span className="display text-2xl">PlantFlow</span>
                    </div>

                    <nav
                        className="order-last -mx-4 flex w-[calc(100%+2rem)] overflow-x-auto px-4 sm:order-0 sm:mx-0 sm:w-auto sm:flex-1 sm:self-stretch sm:overflow-visible sm:px-0"
                        aria-label="Main"
                    >
                        {navigation
                            .filter((item) => canOpen(me, item))
                            .map((item) => (
                                <NavLink
                                    key={item.to}
                                    to={item.to}
                                    className={({ isActive }) =>
                                        `flex shrink-0 items-center border-b-[3px] px-3 py-2.5 text-sm font-medium outline-offset-[-3px] transition-colors focus-visible:outline-2 focus-visible:outline-signal sm:px-4 sm:py-0 ${
                                            isActive
                                                ? "border-signal text-white"
                                                : "border-transparent text-chrome-foreground/65 hover:text-white"
                                        }`
                                    }
                                >
                                    <item.icon
                                        className="mr-2 size-4"
                                        aria-hidden="true"
                                    />
                                    {item.label}
                                </NavLink>
                            ))}
                    </nav>

                    <div className="ml-auto flex items-center gap-3 text-sm">
                        <span className="hidden text-chrome-foreground/65 md:inline">
                            {me.tenantName}
                        </span>
                        <span
                            className="grid size-8 place-items-center rounded-full bg-signal text-xs font-bold text-ink"
                            title={me.email}
                        >
                            {initials}
                        </span>
                        <Button
                            variant="ghost"
                            size="sm"
                            className="gap-2 text-chrome-foreground hover:bg-white/10 hover:text-white"
                            aria-label="Sign out"
                            onClick={() => void signOut()}
                        >
                            <LuLogOut className="size-4" aria-hidden="true" />
                            <span className="hidden sm:inline">Sign out</span>
                        </Button>
                    </div>
                </div>
            </header>

            <main className="mx-auto max-w-352 px-4 py-6 sm:px-5 sm:py-8">
                <Outlet />
            </main>
        </div>
    );
}
