import type { ReactNode } from "react";
import { Mark } from "./Mark";
import { Motif } from "./Motif";

// Shared frame for the screens people see before they can use the app.
export function AuthLayout({ children }: { children: ReactNode }) {
    return (
        <div className="grid min-h-screen lg:grid-cols-2">
            <aside className="hidden flex-col justify-between bg-chrome p-12 text-chrome-foreground lg:flex">
                <div className="flex items-center gap-3">
                    <Mark className="size-9" />
                    <span className="display text-3xl">PlantFlow</span>
                </div>

                <div>
                    <h2 className="display max-w-lg text-7xl leading-[0.92]">
                        Every pour on plan, every truck on record.
                    </h2>
                    <p className="mt-5 max-w-sm text-lg text-chrome-foreground/70">
                        Orders, shift plans, dispatch and invoices in one place,
                        built for ready-mix plants.
                    </p>
                </div>

                <Motif className="w-full max-w-xl" />
            </aside>

            <main className="flex items-center justify-center bg-background p-6 sm:p-12">
                <div className="w-full max-w-sm">
                    <div className="mb-10 flex items-center gap-2.5 lg:hidden">
                        <Mark className="size-8" />
                        <span className="display text-2xl">PlantFlow</span>
                    </div>
                    {children}
                </div>
            </main>
        </div>
    );
}
