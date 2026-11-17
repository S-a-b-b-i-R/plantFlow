import { Navigate } from "react-router-dom";
import { useMe } from "@/auth/useMe";
import { firstAllowed } from "@/layout/navigation";

export function Home() {
    const me = useMe().data!;
    const target = firstAllowed(me);

    if (target) return <Navigate to={target} replace />;

    return (
        <div className="mx-auto max-w-md py-16 text-center">
            <h1 className="display text-3xl">Nothing here for your role yet</h1>
            <p className="mt-3 text-sm text-muted-foreground">
                Your account is active, but none of the screens built so far
                are open to your role. Ask your company admin if you need
                access to the live board or the batching console.
            </p>
        </div>
    );
}
