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
import type { Me } from "@/lib/types";

// Shown to people who are signed in but cannot use the app yet.
export function Waiting({ me }: { me: Me }) {
    const { signOut } = useAuth();
    const pending = me.status === "Pending";

    return (
        <FullScreen>
            <Card className="w-full max-w-sm">
                <CardHeader>
                    <CardTitle>
                        {pending ? "Waiting for approval" : "No access"}
                    </CardTitle>
                    <CardDescription>Signed in as {me.email}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                    <p className="text-sm">
                        {pending
                            ? "An administrator needs to approve your account. This page checks again every few seconds, so it will open by itself once you are approved."
                            : me.status === "Rejected"
                              ? "Your account request was not approved. Ask an administrator if you think this is a mistake."
                              : "Your account has been disabled. Ask an administrator to turn it back on."}
                    </p>
                    <Button variant="outline" onClick={() => void signOut()}>
                        Sign out
                    </Button>
                </CardContent>
            </Card>
        </FullScreen>
    );
}
