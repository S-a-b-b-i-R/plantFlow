import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ReturnRow } from "@/components/operator/ReturnsPanel";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import { cft } from "@/lib/format";
import type { ReturnedConcrete } from "@/lib/types";
import { cn } from "@/lib/utils";

type PlantName = { id: string; name: string };

const tabs = [
  {
    key: "decide",
    label: "To decide",
    statuses: ["AwaitingInspection", "NeedsTreatment"],
    empty:
      "Nothing is waiting for QC. New returns and rejects show up here by themselves.",
  },
  {
    key: "stock",
    label: "Cleared stock",
    statuses: ["Reusable", "Treated"],
    empty: "No cleared concrete is waiting to be used.",
  },
  {
    key: "closed",
    label: "Closed",
    statuses: ["Used", "Dumped"],
    empty: "Nothing has been used or dumped yet.",
  },
] as const;

type TabKey = (typeof tabs)[number]["key"];

const day = (iso: string) =>
  new Date(iso).toLocaleDateString([], { day: "numeric", month: "short" });

export function Quality() {
  const [tab, setTab] = useState<TabKey>("decide");

  const returns = useQuery({
    queryKey: ["returns", "all"],
    queryFn: () => api<ReturnedConcrete[]>("/api/returns"),
    refetchInterval: 15_000,
  });
  // Only for plant names. If it is not available the list still works.
  const plants = useQuery({
    queryKey: ["plants", "names"],
    queryFn: () => api<{ items: PlantName[] }>("/api/plants"),
    staleTime: 5 * 60_000,
  });
  const plantName = new Map(
    (plants.data?.items ?? []).map((p) => [p.id, p.name]),
  );

  const all = returns.data ?? [];
  const current = tabs.find((t) => t.key === tab)!;
  const shown = all.filter((r) => current.statuses.some((s) => s === r.status));
  const toDecideCft = all
    .filter(
      (r) => r.status === "AwaitingInspection" || r.status === "NeedsTreatment",
    )
    .reduce((sum, r) => sum + r.remainingCft, 0);

  return (
    <div className="space-y-5">
      <header>
        <h1 className="display text-4xl leading-none">Quality control</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Decide what happens to concrete that came back or was rejected.
          Cleared concrete can go into a new load of the same product.
          {toDecideCft > 0 && (
            <>
              {" "}
              <span className="font-semibold text-foreground tabular-nums">
                {cft(toDecideCft)} CFT
              </span>{" "}
              is waiting for a decision.
            </>
          )}
        </p>
      </header>

      <div role="tablist" className="flex flex-wrap gap-2">
        {tabs.map((t) => {
          const count = all.filter((r) =>
            t.statuses.some((s) => s === r.status),
          ).length;
          const active = t.key === tab;
          return (
            <button
              key={t.key}
              role="tab"
              aria-selected={active}
              onClick={() => setTab(t.key)}
              className={cn(
                "h-11 rounded-sm border px-4 text-sm font-semibold",
                active
                  ? "border-signal bg-signal text-ink"
                  : "bg-card text-foreground hover:border-ink",
              )}
            >
              {t.label} <span className="tabular-nums opacity-70">{count}</span>
            </button>
          );
        })}
      </div>

      {returns.isPending ? (
        <Skeleton className="h-40 w-full" />
      ) : returns.isError ? (
        <p className="text-sm font-medium text-late" role="alert">
          The returned concrete could not be loaded. {returns.error.message}
        </p>
      ) : shown.length === 0 ? (
        <p className="rounded-sm border bg-card px-4 py-10 text-center text-sm text-muted-foreground">
          {current.empty}
        </p>
      ) : (
        <ul className="divide-y rounded-sm border bg-card">
          {shown.map((item) => (
            <ReturnRow
              key={item.id}
              item={item}
              canDecide
              where={[plantName.get(item.plantId), day(item.createdAtUtc)]
                .filter(Boolean)
                .join(", ")}
            />
          ))}
        </ul>
      )}
      {all.length === 200 && (
        <p className="text-xs text-muted-foreground">Showing the latest 200.</p>
      )}
    </div>
  );
}
