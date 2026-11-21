import { useEffect, useRef, useState } from "react";
import { Outlet } from "react-router-dom";
import { LuMenu, LuX } from "react-icons/lu";
import { useMe } from "@/auth/useMe";
import { Mark } from "@/components/Mark";
import { Sidebar } from "@/layout/Sidebar";
import { initialsOf } from "@/lib/format";
import { cn } from "@/lib/utils";

const collapsedKey = "plantflow.sidebar.collapsed";

// A per-browser preference, so a blocked or empty storage just means the menu starts open.
const readCollapsed = () => {
    try {
        return localStorage.getItem(collapsedKey) === "1";
    } catch {
        return false;
    }
};

export function AppShell() {
    const me = useMe().data!;
    const [collapsed, setCollapsed] = useState(readCollapsed);
    const [drawerOpen, setDrawerOpen] = useState(false);
    const menuButton = useRef<HTMLButtonElement>(null);
    const drawer = useRef<HTMLDivElement>(null);

    const toggleCollapsed = () => {
        const next = !collapsed;
        setCollapsed(next);
        try {
            localStorage.setItem(collapsedKey, next ? "1" : "0");
        } catch {
            // The menu still collapses for this visit.
        }
    };

    const closeDrawer = () => {
        setDrawerOpen(false);
        menuButton.current?.focus();
    };

    // The drawer closes on Escape and takes focus while it is open.
    useEffect(() => {
        if (!drawerOpen) return;
        drawer.current?.querySelector<HTMLElement>("a, button")?.focus();
        const onKey = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                setDrawerOpen(false);
                menuButton.current?.focus();
            }
        };
        document.addEventListener("keydown", onKey);
        return () => document.removeEventListener("keydown", onKey);
    }, [drawerOpen]);

    return (
        <div className="min-h-screen bg-background lg:flex">
            <aside
                className={cn(
                    "sticky top-0 hidden h-screen shrink-0 transition-[width] duration-200 motion-reduce:transition-none lg:block",
                    collapsed ? "w-16" : "w-60",
                )}
            >
                <Sidebar collapsed={collapsed} onToggle={toggleCollapsed} />
            </aside>

            <header className="sticky top-0 z-30 flex h-14 items-center gap-3 bg-chrome px-2 text-chrome-foreground lg:hidden">
                <button
                    ref={menuButton}
                    type="button"
                    aria-label="Open menu"
                    aria-expanded={drawerOpen}
                    aria-controls="app-drawer"
                    onClick={() => setDrawerOpen(true)}
                    className="grid size-11 place-items-center rounded-sm hover:bg-white/10 focus-visible:outline-2 focus-visible:outline-signal"
                >
                    <LuMenu className="size-5" aria-hidden />
                </button>
                <Mark className="size-7" />
                <span className="display text-2xl">PlantFlow</span>
                <span
                    className="ml-auto mr-2 grid size-8 place-items-center rounded-full bg-signal text-xs font-bold text-ink"
                    title={me.email}
                >
                    {initialsOf(me.displayName ?? me.email)}
                </span>
            </header>

            {drawerOpen && (
                <div className="fixed inset-0 z-40 lg:hidden">
                    <div
                        className="absolute inset-0 bg-ink/60"
                        onClick={closeDrawer}
                        aria-hidden="true"
                    />
                    <div
                        id="app-drawer"
                        ref={drawer}
                        role="dialog"
                        aria-modal="true"
                        aria-label="Menu"
                        className="relative h-full w-72 max-w-[85vw] shadow-xl"
                    >
                        <Sidebar
                            collapsed={false}
                            onNavigate={() => setDrawerOpen(false)}
                        />
                        <button
                            type="button"
                            aria-label="Close menu"
                            onClick={closeDrawer}
                            className="absolute top-3 right-3 grid size-11 place-items-center rounded-sm text-chrome-foreground hover:bg-white/10 focus-visible:outline-2 focus-visible:outline-signal"
                        >
                            <LuX className="size-5" aria-hidden />
                        </button>
                    </div>
                </div>
            )}

            <main className="min-w-0 flex-1 px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
                <div className="mx-auto max-w-352">
                    <Outlet />
                </div>
            </main>
        </div>
    );
}
