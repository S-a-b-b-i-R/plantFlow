import { LuCheck } from "react-icons/lu";
import { Link } from "react-router-dom";
import { nextButtonClass, panelClass } from "@/components/orders/styles";
import { useSetup } from "@/components/setup/useSetup";
import { Skeleton } from "@/components/ui/skeleton";
import { stepInfo } from "@/lib/setup";
import { cn } from "@/lib/utils";

// Getting started: what a new company still has to set up before it can take and run an order.
// It only guides; every screen still checks its own rules.
export function Setup() {
    const setup = useSetup();
    const data = setup.data;
    const next = data?.steps.find((s) => !s.done)?.key;

    return (
        <div className="space-y-5">
            <header>
                <h1 className="display text-4xl leading-none">Get started</h1>
                <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                    The things a company sets up once before it takes its first
                    order. Work down the list; each step ticks off as soon as
                    it's done.
                </p>
            </header>

            {setup.isLoading && <Skeleton className="h-96 w-full" />}
            {setup.error && (
                <p role="alert" className="text-sm text-late">
                    {setup.error.message}
                </p>
            )}

            {data && (
                <>
                    <div className="max-w-2xl space-y-2">
                        <p className="text-sm font-medium tabular-nums">
                            {data.done === data.total
                                ? "Everything is set up. You can take orders."
                                : `${data.done} of ${data.total} done`}
                        </p>
                        <div
                            className="h-2 overflow-hidden rounded-full bg-muted"
                            role="progressbar"
                            aria-label="Setup progress"
                            aria-valuemin={0}
                            aria-valuemax={data.total}
                            aria-valuenow={data.done}
                        >
                            <div
                                className="h-full rounded-full bg-ok transition-[width] motion-reduce:transition-none"
                                style={{
                                    width: `${(data.done / data.total) * 100}%`,
                                }}
                            />
                        </div>
                    </div>

                    <ol className={cn(panelClass, "max-w-3xl divide-y")}>
                        {data.steps.map((step, i) => {
                            const info = stepInfo(step.key);
                            const isNext = step.key === next;
                            return (
                                <li
                                    key={step.key}
                                    className={cn(
                                        "flex flex-wrap items-center gap-x-4 gap-y-3 px-4 py-4",
                                        isNext && "bg-signal/10",
                                    )}
                                >
                                    <span
                                        className={cn(
                                            "grid size-8 shrink-0 place-items-center rounded-full border text-sm font-semibold tabular-nums",
                                            step.done &&
                                                "border-ok bg-ok text-white",
                                        )}
                                        aria-hidden
                                    >
                                        {step.done ? (
                                            <LuCheck className="size-4" />
                                        ) : (
                                            i + 1
                                        )}
                                    </span>
                                    <span className="min-w-0 flex-1 basis-60">
                                        <span
                                            className={cn(
                                                "block font-medium",
                                                step.done &&
                                                    "text-muted-foreground",
                                            )}
                                        >
                                            {info.title}
                                            <span className="sr-only">
                                                {step.done
                                                    ? " (done)"
                                                    : " (to do)"}
                                            </span>
                                        </span>
                                        {!step.done && (
                                            <span className="block text-sm text-muted-foreground">
                                                {info.hint}
                                            </span>
                                        )}
                                    </span>
                                    <Link
                                        to={info.to}
                                        className={cn(
                                            "ml-12 inline-flex h-11 items-center rounded-full border px-4 text-sm font-medium hover:border-ink sm:ml-0",
                                            isNext
                                                ? nextButtonClass
                                                : "bg-card",
                                        )}
                                    >
                                        {step.done ? "Review" : info.action}
                                    </Link>
                                </li>
                            );
                        })}
                    </ol>
                </>
            )}
        </div>
    );
}
