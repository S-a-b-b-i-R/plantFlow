import { Outlet } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FullScreen } from "@/components/FullScreen";
import { SignIn } from "@/pages/SignIn";
import { Waiting } from "@/pages/Waiting";
import { useAuth } from "./auth-context";
import { useMe } from "./useMe";

// Decides what a visitor sees: sign-in, a waiting screen, or the app.
export function Gate() {
    const { firebaseUser, signOut } = useAuth();
    const me = useMe();

    if (firebaseUser === undefined) {
        return (
            <FullScreen>
                <p className="text-sm text-muted-foreground">Loading…</p>
            </FullScreen>
        );
    }

    if (firebaseUser === null) return <SignIn />;

    if (me.isPending) {
        return (
            <FullScreen>
                <p className="text-sm text-muted-foreground">
                    Loading your account…
                </p>
            </FullScreen>
        );
    }

    if (me.isError) {
        return (
            <FullScreen>
                <div className="max-w-sm space-y-3 text-center">
                    <p className="text-sm">
                        Could not load your account: {me.error.message}
                    </p>
                    <div className="flex justify-center gap-2">
                        <Button onClick={() => void me.refetch()}>
                            Try again
                        </Button>
                        <Button
                            variant="outline"
                            onClick={() => void signOut()}
                        >
                            Sign out
                        </Button>
                    </div>
                </div>
            </FullScreen>
        );
    }

    if (me.data.status !== "Active") return <Waiting me={me.data} />;

    return <Outlet />;
}
