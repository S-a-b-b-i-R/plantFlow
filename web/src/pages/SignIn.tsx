import { useState } from "react";
import { Button } from "@/components/ui/button";
import { AuthLayout } from "@/components/AuthLayout";
import { useAuth } from "@/auth/auth-context";
import { FcGoogle } from "react-icons/fc";

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
                setError(
                    e instanceof Error
                        ? e.message
                        : "Sign-in failed. Try again.",
                );
            }
        } finally {
            setBusy(false);
        }
    }

    return (
        <AuthLayout>
            <h1 className="display text-5xl">Sign in</h1>
            <p className="mt-3 text-muted-foreground">
                Use your Google account. A new account is approved by an
                administrator before it can open the app.
            </p>
            <Button
                variant="outline"
                size="lg"
                className="mt-8 h-12 w-full gap-3 border-foreground/25 bg-card text-base font-medium hover:bg-card hover:border-foreground/60 hover:cursor-pointer"
                onClick={handleSignIn}
                disabled={busy}
            >
                <FcGoogle className="size-5" aria-hidden="true" />
                {busy ? "Opening Google…" : "Continue with Google"}
            </Button>
            {error && (
                <p role="alert" className="mt-4 text-sm text-destructive">
                    {error}
                </p>
            )}
        </AuthLayout>
    );
}
