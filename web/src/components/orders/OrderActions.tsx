import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { LuTriangleAlert } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "./Field";
import { ReasonAction } from "./ReasonAction";
import { inputClass, nextButtonClass, panelClass } from "./styles";
import { useOrderAct } from "./useOrderAct";
import {
    confirmationLabel,
    type ConfirmationMethod,
    type OrderDetail,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

const linkButton =
    "inline-flex h-10 items-center rounded-sm border border-ink/30 bg-card px-4 text-sm hover:border-ink hover:bg-ink hover:text-white";

// What can be done to this order next, for the person looking at it. One yellow button at most.
export function OrderActions({ detail }: { detail: OrderDetail }) {
    const me = useMe().data!;
    const act = useOrderAct();
    const { order } = detail;
    const url = `/api/orders/${order.id}`;

    const canEdit = can(me, PERMISSIONS.ordersEdit);
    const canConfirm = can(me, PERMISSIONS.ordersConfirm);
    const canRevise = can(me, PERMISSIONS.ordersApproveSalesManager);

    const cancel = canEdit && (
        <ReasonAction
            label="Cancel order"
            tone="danger"
            question="Why is the order being cancelled?"
            submitLabel="Cancel the order"
            pending={act.isPending}
            onSubmit={(reason) =>
                act.mutate({ url: `${url}/cancel`, body: { reason } })
            }
        />
    );

    let heading: string;
    let body: ReactNode;

    switch (order.status) {
        case "Draft":
            if (!canEdit) return null;
            heading = "Ready to send?";
            body = (
                <>
                    {order.belowFloorLineCount > 0 && (
                        <p className="flex items-start gap-2 text-sm text-late">
                            <LuTriangleAlert
                                aria-hidden
                                className="mt-0.5 size-4 shrink-0"
                            />
                            {order.belowFloorLineCount === 1
                                ? "One line is priced under the floor price. Management will have to approve it."
                                : `${order.belowFloorLineCount} lines are priced under the floor price. Management will have to approve them.`}
                        </p>
                    )}
                    <div className="flex flex-wrap gap-2">
                        <Button
                            className={cn("h-11 px-6", nextButtonClass)}
                            disabled={act.isPending}
                            onClick={() => act.mutate({ url: `${url}/submit` })}
                        >
                            {act.isPending
                                ? "Submitting…"
                                : "Submit for approval"}
                        </Button>
                        <Link
                            to={`/orders/${order.id}/edit`}
                            className={linkButton}
                        >
                            Edit
                        </Link>
                        {cancel}
                    </div>
                </>
            );
            break;

        case "Submitted":
        case "InApproval":
            heading = "Waiting for approval";
            body = (
                <div className="flex flex-wrap gap-2">
                    {canRevise && (
                        <Link
                            to={`/orders/${order.id}/edit?mode=revise`}
                            className={linkButton}
                        >
                            Revise the order
                        </Link>
                    )}
                    {cancel}
                    {!canRevise && !canEdit && (
                        <p className="text-sm text-muted-foreground">
                            Nothing for you to do here.
                        </p>
                    )}
                </div>
            );
            break;

        case "Approved":
            heading = "Approved. Has the customer said yes?";
            body = (
                <div className="space-y-3">
                    {canConfirm && (
                        <ConfirmForm
                            pending={act.isPending}
                            onConfirm={(b) =>
                                act.mutate({ url: `${url}/confirm`, body: b })
                            }
                        />
                    )}
                    <div className="flex flex-wrap gap-2">{cancel}</div>
                    {!canConfirm && !canEdit && (
                        <p className="text-sm text-muted-foreground">
                            The Sales Manager records the customer's go-ahead.
                        </p>
                    )}
                </div>
            );
            break;

        case "Confirmed":
        case "InProgress":
            if (
                !canConfirm &&
                !canRevise &&
                !(canEdit && order.status === "Confirmed")
            )
                return null;
            heading = order.status === "Confirmed" ? "Confirmed" : "Delivering";
            body = (
                <div className="flex flex-wrap gap-2">
                    {canRevise && order.status === "Confirmed" && (
                        <Link
                            to={`/orders/${order.id}/edit?mode=amend`}
                            className={linkButton}
                        >
                            Amend the order
                        </Link>
                    )}
                    {canConfirm && (
                        <ReasonAction
                            label="Put on hold"
                            question="Why is it being held?"
                            submitLabel="Put on hold"
                            pending={act.isPending}
                            onSubmit={(reason) =>
                                act.mutate({
                                    url: `${url}/hold`,
                                    body: { reason },
                                })
                            }
                        />
                    )}
                    {order.status === "Confirmed" && cancel}
                </div>
            );
            break;

        case "OnHold":
            if (!canConfirm && !canEdit) return null;
            heading = "On hold";
            body = (
                <div className="flex flex-wrap gap-2">
                    {canConfirm && (
                        <Button
                            className={cn("h-11 px-6", nextButtonClass)}
                            disabled={act.isPending}
                            onClick={() =>
                                act.mutate({ url: `${url}/release` })
                            }
                        >
                            Release the hold
                        </Button>
                    )}
                    {cancel}
                </div>
            );
            break;

        default:
            return null;
    }

    return (
        <section
            className={cn(panelClass, "space-y-3 p-4")}
            aria-label="Next step"
        >
            <h2 className="display text-2xl">{heading}</h2>
            {body}
            {act.isError && (
                <p className="text-sm font-medium text-late" role="alert">
                    {act.error.message}
                </p>
            )}
        </section>
    );
}

function ConfirmForm({
    pending,
    onConfirm,
}: {
    pending: boolean;
    onConfirm: (body: {
        method: ConfirmationMethod;
        reference?: string;
    }) => void;
}) {
    const [method, setMethod] = useState<ConfirmationMethod>("Phone");
    const [reference, setReference] = useState("");

    return (
        <div className="space-y-3">
            <div className="grid gap-3 sm:grid-cols-2">
                <Field label="How did they say yes?">
                    <select
                        value={method}
                        onChange={(e) =>
                            setMethod(e.target.value as ConfirmationMethod)
                        }
                        className={inputClass}
                    >
                        {(
                            Object.keys(
                                confirmationLabel,
                            ) as ConfirmationMethod[]
                        ).map((m) => (
                            <option key={m} value={m}>
                                {confirmationLabel[m]}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field
                    label="Reference"
                    hint="For example the purchase order number."
                >
                    <input
                        value={reference}
                        onChange={(e) => setReference(e.target.value)}
                        maxLength={100}
                        className={inputClass}
                    />
                </Field>
            </div>
            <Button
                className={cn("h-11 px-6", nextButtonClass)}
                disabled={pending}
                onClick={() =>
                    onConfirm({
                        method,
                        reference: reference.trim() || undefined,
                    })
                }
            >
                Confirm the order
            </Button>
        </div>
    );
}
