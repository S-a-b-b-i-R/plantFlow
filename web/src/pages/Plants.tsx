import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { LuChevronRight, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import { PlantForm } from "@/components/plants/PlantForms";
import { StatusChip } from "@/components/plants/StatusChip";
import { usePlantList, type StatusFilter } from "@/components/plants/usePlants";
import { Skeleton } from "@/components/ui/skeleton";
import { cft } from "@/lib/format";
import { cn } from "@/lib/utils";

// Every plant of the company. Opening one shows its shifts and batching plants.
export function Plants() {
    const me = useMe().data!;
    const canManage = can(me, PERMISSIONS.plantsManage);
    const navigate = useNavigate();
    const [search, setSearch] = useState("");
    const [status, setStatus] = useState<StatusFilter>("Active");
    const [adding, setAdding] = useState(false);
    const plants = usePlantList(search, status);
    const items = plants.data?.items ?? [];

    return (
        <div className="mx-auto max-w-3xl space-y-4">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">Plants</h1>
                    <p className="mt-2 text-sm text-muted-foreground">
                        Where concrete is made. Each plant has its working
                        shifts and the batching plants that mix the concrete.
                    </p>
                </div>
                {canManage && !adding && (
                    <Button
                        className={cn("h-11 px-4", nextButtonClass)}
                        onClick={() => setAdding(true)}
                    >
                        <LuPlus aria-hidden /> New plant
                    </Button>
                )}
            </header>

            {adding && (
                <PlantForm
                    plant={null}
                    onDone={(saved) => {
                        setAdding(false);
                        if (saved) navigate(`/plants/${saved.plant.id}`);
                    }}
                    onCancel={() => setAdding(false)}
                />
            )}

            <div className="flex flex-wrap gap-2">
                <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search code or name"
                    aria-label="Search plants"
                    className={cn(inputClass, "h-11 min-w-0 flex-1")}
                />
                <select
                    value={status}
                    onChange={(e) => setStatus(e.target.value as StatusFilter)}
                    aria-label="Show"
                    className={cn(inputClass, "h-11 w-auto")}
                >
                    <option value="Active">Active</option>
                    <option value="Inactive">Inactive</option>
                    <option value="All">All</option>
                </select>
            </div>

            {plants.isLoading && <Skeleton className="h-40 w-full" />}
            {plants.isError && (
                <p role="alert" className="text-sm text-late">
                    {plants.error.message}
                </p>
            )}
            {plants.data && items.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    {search.trim() || status !== "Active"
                        ? "No plants match."
                        : canManage
                          ? "No plants yet. Add the first plant to start taking orders."
                          : "No plants yet. Ask an admin or plant manager to add one."}
                </p>
            )}

            {items.length > 0 && (
                <ul className={cn(panelClass, "divide-y")}>
                    {items.map((plant) => (
                        <li key={plant.id}>
                            <Link
                                to={`/plants/${plant.id}`}
                                className="flex min-h-14 items-center gap-3 px-4 py-3 hover:bg-muted/50"
                            >
                                <div className="min-w-0 flex-1">
                                    <span className="flex flex-wrap items-center gap-2">
                                        <span className="font-semibold">
                                            {plant.code}
                                        </span>
                                        <span className="truncate">
                                            {plant.name}
                                        </span>
                                        {plant.status === "Inactive" && (
                                            <StatusChip status={plant.status} />
                                        )}
                                    </span>
                                    <span className="mt-0.5 block text-xs text-muted-foreground">
                                        {plant.batchingPlantCount === 1
                                            ? "1 batching plant"
                                            : `${plant.batchingPlantCount} batching plants`}{" "}
                                        · {cft(plant.dailyCapacityCft)} CFT a
                                        day
                                    </span>
                                </div>
                                <LuChevronRight
                                    aria-hidden
                                    className="size-5 shrink-0 text-muted-foreground"
                                />
                            </Link>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
