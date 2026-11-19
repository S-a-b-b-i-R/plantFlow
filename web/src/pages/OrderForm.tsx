import { useQuery } from "@tanstack/react-query";
import { Navigate, useParams, useSearchParams } from "react-router-dom";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OrderWizard, type WizardMode } from "@/components/orders/OrderWizard";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api";
import type { OrderDetail } from "@/lib/orders";

// One page for a new order and for the three ways to change an existing one.
export function OrderForm() {
    const me = useMe().data!;
    const { id } = useParams();
    const [search] = useSearchParams();

    const mode: WizardMode = !id
        ? "create"
        : search.get("mode") === "revise"
          ? "revise"
          : search.get("mode") === "amend"
            ? "amend"
            : "edit";

    const order = useQuery({
        queryKey: ["order", id],
        queryFn: () => api<OrderDetail>(`/api/orders/${id}`),
        enabled: !!id,
    });

    const allowed =
        mode === "create"
            ? can(me, PERMISSIONS.ordersCreate)
            : mode === "edit"
              ? can(me, PERMISSIONS.ordersEdit)
              : can(me, PERMISSIONS.ordersApproveSalesManager);
    if (!allowed)
        return <Navigate to={id ? `/orders/${id}` : "/orders"} replace />;

    if (mode === "create") return <OrderWizard mode="create" />;
    if (order.isPending) return <Skeleton className="mx-auto h-96 max-w-3xl" />;
    if (order.isError) {
        return (
            <p className="text-sm font-medium text-late" role="alert">
                The order could not be loaded. {order.error.message}
            </p>
        );
    }

    // Keyed so a refreshed order starts the form again instead of mixing old and new values.
    return (
        <OrderWizard
            key={`${order.data.order.id}-${mode}`}
            mode={mode}
            existing={order.data}
        />
    );
}
