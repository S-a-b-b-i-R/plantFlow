import type { ReactNode } from "react";
import { NavLink } from "react-router-dom";
import { LuLogOut, LuPanelLeftClose, LuPanelLeftOpen } from "react-icons/lu";
import { useAuth } from "@/auth/auth-context";
import { useMe } from "@/auth/useMe";
import { Mark } from "@/components/Mark";
import { OpenedBadge } from "@/components/customers/OpenedBadge";
import { CloseBadge } from "@/components/ledger/CloseBadge";
import { ApprovalBadge } from "@/components/surveys/ApprovalBadge";
import { SetupBadge } from "@/components/setup/SetupBadge";
import { PendingUsersBadge } from "@/components/users/PendingUsersBadge";
import { canOpen, navGroups, navigation } from "@/layout/navigation";
import { initialsOf } from "@/lib/format";
import { cn } from "@/lib/utils";

// Counts that ride along with a menu item. They render nothing when there is nothing to show.
const badges: Record<string, ReactNode> = {
    "/surveys": <ApprovalBadge />,
    "/customers": <OpenedBadge />,
    "/users": <PendingUsersBadge />,
    "/setup": <SetupBadge />,
    "/ledger": <CloseBadge />,
};

// The app's menu, grouped by the work people do. On wide screens it can shrink to a rail of icons;
// on narrow screens the same menu opens as a drawer.
export function Sidebar({
    collapsed,
    onToggle,
    onNavigate,
}: {
    collapsed: boolean;
    // Absent in the drawer, which closes instead of collapsing.
    onToggle?: () => void;
    onNavigate?: () => void;
}) {
    const { signOut } = useAuth();
    const me = useMe().data!;
    const name = me.displayName ?? me.email;
    const groups = navGroups
        .map((group) => ({
            group,
            items: navigation.filter(
                (item) => item.group === group && canOpen(me, item),
            ),
        }))
        .filter((g) => g.items.length > 0);

    return (
        <div className="flex h-full flex-col bg-chrome text-chrome-foreground">
            <div
                className={cn(
                    "flex h-16 shrink-0 items-center gap-3 border-b border-white/10",
                    collapsed ? "justify-center px-2" : "px-5",
                )}
            >
                <Mark className="size-8 shrink-0" />
                {!collapsed && (
                    <div className="min-w-0 leading-tight">
                        <span className="display block text-2xl leading-none">
                            PlantFlow
                        </span>
                        <span className="mt-1 block truncate text-xs text-chrome-foreground/60">
                            {me.tenantName}
                        </span>
                    </div>
                )}
            </div>

            <nav
                aria-label="Main"
                className="flex-1 space-y-4 overflow-y-auto overscroll-contain px-3 py-4 [scrollbar-color:rgb(255_255_255/0.18)_transparent] [scrollbar-width:thin]"
            >
                {groups.map(({ group, items }) => (
                    <div key={group}>
                        {collapsed ? (
                            <div
                                className="mx-2 mb-2 border-t border-white/10 first:hidden"
                                aria-hidden="true"
                            />
                        ) : (
                            <p className="mb-1.5 px-3 text-xs font-medium text-chrome-foreground/55">
                                {group}
                            </p>
                        )}
                        <ul className="space-y-0.5">
                            {items.map((item) => (
                                <li key={item.to}>
                                    <NavLink
                                        to={item.to}
                                        onClick={onNavigate}
                                        title={
                                            collapsed ? item.label : undefined
                                        }
                                        aria-label={
                                            collapsed ? item.label : undefined
                                        }
                                        className={({ isActive }) =>
                                            cn(
                                                "relative flex h-9 items-center gap-3 rounded-sm text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-signal",
                                                collapsed
                                                    ? "justify-center px-0"
                                                    : "px-3",
                                                isActive
                                                    ? "bg-white/[0.08] text-white before:absolute before:inset-y-1.5 before:-left-3 before:w-[3px] before:rounded-r-sm before:bg-signal"
                                                    : "text-chrome-foreground/70 hover:bg-white/[0.05] hover:text-white",
                                            )
                                        }
                                    >
                                        <item.icon
                                            className="size-[18px] shrink-0"
                                            aria-hidden="true"
                                        />
                                        {!collapsed && (
                                            <span className="truncate">
                                                {item.label}
                                            </span>
                                        )}
                                        {badges[item.to] && (
                                            <span
                                                className={cn(
                                                    collapsed
                                                        ? "absolute top-0 right-1 scale-90"
                                                        : "ml-auto",
                                                )}
                                            >
                                                {badges[item.to]}
                                            </span>
                                        )}
                                    </NavLink>
                                </li>
                            ))}
                        </ul>
                    </div>
                ))}
            </nav>

            <div className="space-y-0.5 border-t border-white/10 p-3">
                <div
                    className={cn(
                        "flex items-center gap-3",
                        collapsed ? "flex-col py-1" : "py-1 pl-2",
                    )}
                >
                    <span
                        className="grid size-8 shrink-0 place-items-center rounded-full bg-signal text-xs font-bold text-ink"
                        title={collapsed ? `${name} (${me.email})` : undefined}
                    >
                        {initialsOf(name)}
                    </span>
                    {!collapsed && (
                        <div className="min-w-0 flex-1 text-sm leading-tight">
                            <span className="block truncate font-medium text-white">
                                {name}
                            </span>
                            {me.displayName && (
                                <span className="block truncate text-xs text-chrome-foreground/60">
                                    {me.email}
                                </span>
                            )}
                        </div>
                    )}
                    <button
                        type="button"
                        onClick={() => void signOut()}
                        title="Sign out"
                        aria-label="Sign out"
                        className="grid size-9 shrink-0 place-items-center rounded-sm text-chrome-foreground/70 transition-colors hover:bg-white/[0.06] hover:text-white focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-signal"
                    >
                        <LuLogOut className="size-[18px]" aria-hidden />
                    </button>
                </div>
                {onToggle && (
                    <SideButton
                        collapsed={collapsed}
                        label={collapsed ? "Expand menu" : "Collapse menu"}
                        icon={
                            collapsed ? (
                                <LuPanelLeftOpen
                                    className="size-[18px]"
                                    aria-hidden
                                />
                            ) : (
                                <LuPanelLeftClose
                                    className="size-[18px]"
                                    aria-hidden
                                />
                            )
                        }
                        onClick={onToggle}
                    />
                )}
            </div>
        </div>
    );
}

function SideButton({
    collapsed,
    label,
    icon,
    onClick,
}: {
    collapsed: boolean;
    label: string;
    icon: ReactNode;
    onClick: () => void;
}) {
    return (
        <button
            type="button"
            onClick={onClick}
            title={collapsed ? label : undefined}
            aria-label={collapsed ? label : undefined}
            className={cn(
                "flex h-9 w-full items-center gap-3 rounded-sm text-sm font-medium text-chrome-foreground/70 transition-colors hover:bg-white/[0.05] hover:text-white focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-signal",
                collapsed ? "justify-center" : "px-3",
            )}
        >
            {icon}
            {!collapsed && label}
        </button>
    );
}
