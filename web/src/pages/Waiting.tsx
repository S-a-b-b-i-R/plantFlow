import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-context";
import { AuthLayout } from "@/components/AuthLayout";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import type { Me } from "@/lib/types";

// Shown to people who are signed in but cannot use the app yet: no company, waiting for approval,
// rejected, disabled, or in a company the platform has paused.
export function Waiting({ me }: { me: Me }) {
    const { signOut } = useAuth();

    return (
        <AuthLayout>
            {!me.hasCompany && me.status === "Pending" ? (
                <JoinCompany />
            ) : (
                <Status me={me} />
            )}
            <p className="mt-8 text-sm">
                Signed in as <span className="font-medium">{me.email}</span>
            </p>
            <Button
                variant="outline"
                size="lg"
                className="mt-4 w-full"
                onClick={() => void signOut()}
            >
                Sign out
            </Button>
        </AuthLayout>
    );
}

// After a change to who the person is or where they belong, read /api/me again.
function useMeChange<T>(send: (input: T) => Promise<unknown>) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: send,
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ["me"] }),
    });
}

function JoinCompany() {
    const [code, setCode] = useState("");
    const join = useMeChange((value: string) =>
        api("/api/join", {
            method: "POST",
            body: JSON.stringify({ code: value }),
        }),
    );
    const ready = code.replace(/[^a-z0-9]/gi, "").length === 8;

    return (
        <>
            <h1 className="display text-5xl">Join your company</h1>
            <p className="mt-3 text-muted-foreground">
                Enter the join code your company's administrator gave you. They
                approve you, and then the app opens.
            </p>
            <form
                className="mt-6 space-y-3"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (ready) join.mutate(code);
                }}
            >
                <label
                    className="block text-sm font-medium"
                    htmlFor="join-code"
                >
                    Join code
                </label>
                <input
                    id="join-code"
                    value={code}
                    onChange={(e) => {
                        setCode(e.target.value.toUpperCase());
                        join.reset();
                    }}
                    placeholder="7KQM-4PXR"
                    autoComplete="off"
                    autoCapitalize="characters"
                    spellCheck={false}
                    maxLength={12}
                    className="h-14 w-full rounded-sm border bg-card px-4 font-mono text-2xl tracking-[0.2em] uppercase outline-none focus-visible:border-ink focus-visible:ring-2 focus-visible:ring-ink/20"
                />
                {join.error && (
                    <p role="alert" className="text-sm font-medium text-late">
                        {join.error.message}
                    </p>
                )}
                <Button
                    type="submit"
                    size="lg"
                    className="w-full"
                    disabled={!ready || join.isPending}
                >
                    {join.isPending ? "Joining…" : "Join"}
                </Button>
            </form>
        </>
    );
}

function Status({ me }: { me: Me }) {
    const leave = useMeChange(() => api("/api/join/leave", { method: "POST" }));

    if (me.companySuspended) {
        return (
            <>
                <h1 className="display text-5xl">Access paused</h1>
                <p className="mt-3 text-muted-foreground">
                    {me.tenantName}'s access to PlantFlow is paused. Nothing has
                    been deleted. Ask your company's administrator; this page
                    opens the app again as soon as access is back.
                </p>
            </>
        );
    }

    if (me.status === "Pending") {
        return (
            <>
                <h1 className="display text-5xl">Waiting for approval</h1>
                <p className="mt-3 text-muted-foreground">
                    You asked to join{" "}
                    <span className="font-medium text-foreground">
                        {me.tenantName}
                    </span>
                    . Their administrator needs to approve you. This page checks
                    every few seconds and opens the app as soon as you are
                    approved.
                </p>
                <div className="mt-6 border-t pt-4 text-sm">
                    <p className="text-muted-foreground">
                        Not your company? Leave it and enter the right code.
                    </p>
                    <Button
                        variant="outline"
                        className="mt-3 h-11"
                        disabled={leave.isPending}
                        onClick={() => leave.mutate(undefined)}
                    >
                        Leave {me.tenantName}
                    </Button>
                    {leave.error && (
                        <p role="alert" className="mt-2 font-medium text-late">
                            {leave.error.message}
                        </p>
                    )}
                </div>
            </>
        );
    }

    const copy =
        me.status === "Rejected"
            ? {
                  title: "Account not approved",
                  body: `${me.tenantName}'s administrator did not approve this account. If that looks wrong, ask them to review it.`,
              }
            : {
                  title: "Account disabled",
                  body: "This account has been turned off. Ask your company's administrator to turn it back on.",
              };
    return (
        <>
            <h1 className="display text-5xl">{copy.title}</h1>
            <p className="mt-3 text-muted-foreground">{copy.body}</p>
        </>
    );
}
