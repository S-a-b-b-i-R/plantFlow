import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
} from "@/components/ui/card";
import { FullScreen } from "@/components/FullScreen";
import { useAuth } from "@/auth/auth-context";

export function SignIn() {
    const { signInWithGoogle } = useAuth();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    async function handleSignIn() {
        setBusy(true);
        setError(null);
        try {
            await signInWithGoogle();
        } catch (e) {
            // Closing the popup is not an error worth showing.
            const code = (e as { code?: string }).code;
            if (
                code !== "auth/popup-closed-by-user" &&
                code !== "auth/cancelled-popup-request"
            ) {
                setError(e instanceof Error ? e.message : "Sign-in failed.");
            }
        } finally {
            setBusy(false);
        }
    }

    return (
        <FullScreen>
            <Card className="w-full max-w-sm">
                <CardHeader>
                    <CardTitle className="text-2xl">PlantFlow</CardTitle>
                    <CardDescription>
                        Ready-mix operations, from order to invoice.
                    </CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                    <Button
                        className="w-full"
                        onClick={handleSignIn}
                        disabled={busy}
                    >
                        {busy ? "Opening Google…" : "Sign in with Google"}
                    </Button>
                    {error && (
                        <p className="text-sm text-destructive">{error}</p>
                    )}
                    <p className="text-xs text-muted-foreground">
                        New accounts wait for an administrator to approve them
                        before they can use the app.
                    </p>
                </CardContent>
            </Card>
        </FullScreen>
    );
}
