import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { ApprovalsPanel } from "@/components/orders/ApprovalsPanel";
import { BillingPanel } from "@/components/orders/BillingPanel";
import { HistoryPanel } from "@/components/orders/HistoryPanel";
import { Journey } from "@/components/orders/Journey";
import { LinesPanel } from "@/components/orders/LinesPanel";
import { OrderActions } from "@/components/orders/OrderActions";
import { StatusChip } from "@/components/orders/StatusChip";
import { SurveyPanel } from "@/components/orders/SurveyPanel";
import { panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError, api } from "@/lib/api";
import { cft } from "@/lib/format";
import {
    confirmationLabel,
    dateTime,
    money,
    sentBackReason,
    shortDate,
    type OrderDetail,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

export function OrderPage() {
    const { id } = useParams();
    const query = useQuery({
        queryKey: ["order", id],
        queryFn: () => api<OrderDetail>(`/api/orders/${id}`),
        refetchInterval: 30_000,
    });

    if (query.isPending) return <Skeleton className="h-96 w-full" />;
    if (query.isError) {
        const missing =
            query.error instanceof ApiError && query.error.status === 404;
        return (
            <div className="space-y-3">
                <p className="text-sm font-medium text-late" role="alert">
                    {missing
                        ? "That order does not exist."
                        : `The order could not be loaded. ${query.error.message}`}
                </p>
                <Link to="/orders" className="text-sm underline">
                    Back to orders
                </Link>
            </div>
        );
    }

    const detail = query.data;
    const { order } = detail;
    const sentBack =
        order.status === "Draft" ? sentBackReason(detail.history) : null;
    // While approvals are open they are what the viewer came for, so they come before the details.
    const deciding =
        order.status === "Submitted" || order.status === "InApproval";
    const showBilling =
        order.status === "Confirmed" ||
        order.status === "InProgress" ||
        order.status === "Completed";

    return (
        <div className="space-y-5">
            <header className="space-y-3">
                <Link
                    to="/orders"
                    className="text-sm text-muted-foreground hover:text-foreground"
                >
                    Back to orders
                </Link>
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                    <h1 className="display text-4xl leading-none">
                        {order.number}
                    </h1>
                    <StatusChip status={order.status} className="text-sm" />
                </div>
                <p className="text-lg">
                    <span className="font-semibold">{order.customerName}</span>
                    <span className="text-muted-foreground">
                        , {order.siteName}
                    </span>
                </p>
                <Journey status={order.status} />
            </header>

            {sentBack && (
                <p className="rounded-sm border border-late/40 bg-late/5 px-4 py-3 text-sm">
                    <span className="font-semibold text-late">Sent back: </span>
                    {sentBack}
                </p>
            )}
            {order.status === "OnHold" && order.holdReason && (
                <p className="rounded-sm border border-late/40 bg-late/5 px-4 py-3 text-sm">
                    <span className="font-semibold text-late">On hold: </span>
                    {order.holdReason}
                </p>
            )}
            {order.status === "Cancelled" && order.cancelReason && (
                <p className="rounded-sm border bg-muted px-4 py-3 text-sm">
                    <span className="font-semibold">Cancelled: </span>
                    {order.cancelReason}
                </p>
            )}

            <OrderActions detail={detail} />

            {deciding && (
                <ApprovalsPanel
                    orderId={order.id}
                    status={order.status}
                    approvals={detail.approvals}
                />
            )}

            <dl
                className={cn(
                    panelClass,
                    "grid gap-x-6 gap-y-3 p-4 text-sm sm:grid-cols-2 lg:grid-cols-4",
                )}
            >
                <Fact
                    label="Needed on"
                    value={shortDate(order.requestedDate)}
                />
                <Fact label="Plant" value={order.plantCode} />
                <Fact label="Distance" value={`${order.distanceKm} km`} />
                <Fact label="Sales rep" value={order.salesRepName ?? "None"} />
                <Fact
                    label="Quantity"
                    value={`${cft(order.totalQuantityCft)} CFT`}
                />
                <Fact
                    label="Value before VAT"
                    value={money(order.totalValue)}
                />
                <Fact
                    label="Payment terms"
                    value={order.paymentTerms ?? "Not set"}
                />
                {order.confirmedAtUtc && order.confirmationMethod && (
                    <Fact
                        label="Customer said yes"
                        value={`${confirmationLabel[order.confirmationMethod]}${order.confirmationReference ? `, ${order.confirmationReference}` : ""}, ${dateTime(order.confirmedAtUtc)}`}
                    />
                )}
                {order.notes && <Fact label="Notes" value={order.notes} wide />}
            </dl>

            <LinesPanel lines={detail.lines} charges={detail.charges} />

            <SurveyPanel order={order} />

            {!deciding && (
                <ApprovalsPanel
                    orderId={order.id}
                    status={order.status}
                    approvals={detail.approvals}
                />
            )}

            {showBilling && (
                <BillingPanel orderId={order.id} status={order.status} />
            )}

            <HistoryPanel history={detail.history} />
        </div>
    );
}

function Fact({
    label,
    value,
    wide,
}: {
    label: string;
    value: string;
    wide?: boolean;
}) {
    return (
        <div className={wide ? "sm:col-span-2 lg:col-span-4" : undefined}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="font-medium">{value}</dd>
        </div>
    );
}
