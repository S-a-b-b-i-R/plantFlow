import { useSetup } from "./useSetup";

// Steps still to do, next to Get started in the menu. Nothing once the company is set up.
export function SetupBadge() {
    const setup = useSetup();
    const left = setup.data ? setup.data.total - setup.data.done : 0;
    if (left === 0) return null;

    return (
        <span
            className="grid h-5 min-w-5 place-items-center rounded-full bg-signal px-1.5 text-xs font-bold text-ink"
            aria-label={`${left} setup steps left`}
        >
            {left}
        </span>
    );
}
