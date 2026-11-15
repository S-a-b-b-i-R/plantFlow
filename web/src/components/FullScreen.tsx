import type { ReactNode } from "react";

// A centred panel for states that fill the screen: loading, sign-in, waiting.
export function FullScreen({ children }: { children: ReactNode }) {
    return (
        <div className="flex min-h-screen items-center justify-center bg-muted/40 p-4">
            {children}
        </div>
    );
}
