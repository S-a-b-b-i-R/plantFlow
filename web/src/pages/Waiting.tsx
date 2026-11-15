import { Button } from "@/components/ui/button";
import { AuthLayout } from "@/components/AuthLayout";
import { useAuth } from "@/auth/auth-context";
import type { Me } from "@/lib/types";

// Shown to people who are signed in but cannot use the app yet.
export function Waiting({ me }: { me: Me }) {
    const { signOut } = useAuth();

    const copy =
        me.status === "Pending"
            ? {
                  title: "Waiting for approval",
                  body: "An administrator needs to approve your account. This page checks every few seconds and opens the app as soon as you are approved.",
              }
            : me.status === "Rejected"
              ? {
                    title: "Account not approved",
                    body: "An administrator did not approve this account. If that looks wrong, ask them to review it.",
                }
              : {
                    title: "Account disabled",
                    body: "This account has been turned off. Ask an administrator to turn it back on.",
                };

    return (
        <AuthLayout>
            <h1 className="display text-5xl">{copy.title}</h1>
            <p className="mt-3 text-muted-foreground">{copy.body}</p>
            <p className="mt-6 text-sm">
                Signed in as <span className="font-medium">{me.email}</span>
            </p>
            <Button
                variant="outline"
                size="lg"
                className="mt-6 w-full"
                onClick={() => void signOut()}
            >
                Sign out
            </Button>
        </AuthLayout>
    );
}
