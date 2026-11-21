import { useState } from "react";
import { LuCheck, LuCopy, LuRefreshCw } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useJoinCode, useRenewJoinCode } from "./useUsers";

// The company's join code. New people sign in, enter it, and show up under Waiting for approval.
export function JoinCodeCard() {
    const joinCode = useJoinCode();
    const renew = useRenewJoinCode();
    const [copied, setCopied] = useState(false);
    const [confirming, setConfirming] = useState(false);

    return (
        <section
            className={cn(
                panelClass,
                "flex flex-wrap items-center gap-x-6 gap-y-3 p-4",
            )}
        >
            <div className="min-w-0 flex-1 basis-64">
                <h2 className="font-semibold">Join code</h2>
                <p className="text-sm text-muted-foreground">
                    Give this to new staff. They sign in, enter it, and wait
                    here for you to approve them.
                </p>
            </div>
            {joinCode.isLoading && <Skeleton className="h-11 w-44" />}
            {joinCode.error && (
                <p role="alert" className="text-sm text-late">
                    {joinCode.error.message}
                </p>
            )}
            {joinCode.data && (
                <div className="flex flex-wrap items-center gap-2">
                    <code className="rounded-sm border bg-background px-3 py-2 font-mono text-2xl font-semibold tracking-[0.15em]">
                        {joinCode.data.code}
                    </code>
                    <Button
                        variant="outline"
                        className="h-11 px-3"
                        aria-label="Copy the join code"
                        onClick={() => {
                            void navigator.clipboard
                                ?.writeText(joinCode.data.code)
                                .then(() => setCopied(true));
                        }}
                    >
                        {copied ? (
                            <LuCheck aria-hidden className="size-4" />
                        ) : (
                            <LuCopy aria-hidden className="size-4" />
                        )}
                        {copied ? "Copied" : "Copy"}
                    </Button>
                    {confirming ? (
                        <>
                            <span className="text-sm">
                                The old code stops working.
                            </span>
                            <Button
                                tone="danger"
                                className="h-11 px-3"
                                disabled={renew.isPending}
                                onClick={() =>
                                    renew.mutate(undefined, {
                                        onSuccess: () => {
                                            setConfirming(false);
                                            setCopied(false);
                                        },
                                    })
                                }
                            >
                                New code
                            </Button>
                            <Button
                                variant="outline"
                                className="h-11 px-3"
                                onClick={() => setConfirming(false)}
                            >
                                Keep this one
                            </Button>
                        </>
                    ) : (
                        <Button
                            variant="outline"
                            className="h-11 px-3"
                            onClick={() => setConfirming(true)}
                        >
                            <LuRefreshCw aria-hidden className="size-4" />
                            New code
                        </Button>
                    )}
                </div>
            )}
            {renew.error && (
                <p role="alert" className="w-full text-sm text-late">
                    {renew.error.message}
                </p>
            )}
        </section>
    );
}
