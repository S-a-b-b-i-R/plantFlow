import { LuCheck, LuClock, LuMinus, LuX } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { ReasonAction } from "./ReasonAction";
import { panelClass } from "./styles";
import { useOrderAct } from "./useOrderAct";
import {
    dateTime,
    stageLabel,
    type ApprovalDecision,
    type ApprovalStage,
    type OrderApproval,
    type OrderStatus,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

const permissionFor: Partial<Record<ApprovalStage, string>> = {
    SalesManager: PERMISSIONS.ordersApproveSalesManager,
    Management: PERMISSIONS.ordersApproveManagement,
    Credit: PERMISSIONS.ordersApproveCredit,
};
const pathFor: Partial<Record<ApprovalStage, string>> = {
    SalesManager: "sales-manager",
    Management: "management",
    Credit: "credit",
};

const decisionText: Record<ApprovalDecision, string> = {
    Pending: "Waiting",
    Approved: "Approved",
    Rejected: "Sent back",
    Withdrawn: "Not needed now",
};

export function ApprovalsPanel({
    orderId,
    status,
    approvals,
}: {
    orderId: string;
    status: OrderStatus;
    approvals: OrderApproval[];
}) {
    const me = useMe().data!;
    const act = useOrderAct();

    if (approvals.length === 0) return null;

    const latest = Math.max(...approvals.map((a) => a.round));
    const current = approvals.filter((a) => a.round === latest);
    const earlier = approvals.filter((a) => a.round < latest);
    const deciding = status === "Submitted" || status === "InApproval";

    const decide = (
        stage: ApprovalStage,
        verb: "approve" | "reject",
        comment: string,
    ) =>
        act.mutate({
            url: `/api/orders/${orderId}/approvals/${pathFor[stage]}/${verb}`,
            body: { comment },
        });

    return (
        <section className={panelClass} aria-label="Approvals">
            <header className="border-b px-4 py-3">
                <h2 className="display text-2xl">Approvals</h2>
                <p className="text-xs text-muted-foreground">
                    The Sales Manager goes first. Management is needed when a
                    line is under the floor price, and Credit when the company
                    requires it.
                </p>
            </header>
            <ul className="divide-y">
                {current.map((a) => {
                    const permission = permissionFor[a.stage];
                    const mine =
                        deciding &&
                        a.decision === "Pending" &&
                        !!permission &&
                        can(me, permission);
                    return (
                        <li
                            key={`${a.stage}-${a.openedAtUtc}`}
                            className="px-4 py-3"
                        >
                            <div className="flex flex-wrap items-center gap-3">
                                <DecisionIcon decision={a.decision} />
                                <span className="font-semibold">
                                    {stageLabel[a.stage]}
                                </span>
                                <span
                                    className={cn(
                                        "text-sm",
                                        a.decision === "Rejected"
                                            ? "text-late"
                                            : "text-muted-foreground",
                                    )}
                                >
                                    {decisionText[a.decision]}
                                    {a.decidedAtUtc
                                        ? `, ${dateTime(a.decidedAtUtc)}`
                                        : ""}
                                </span>
                            </div>
                            {a.comment && (
                                <p className="mt-1 pl-8 text-sm">
                                    “{a.comment}”
                                </p>
                            )}
                            {mine && (
                                <div className="mt-3 flex flex-wrap gap-2 pl-8">
                                    <ReasonAction
                                        label="Approve"
                                        question="Add a comment (optional)"
                                        submitLabel="Approve"
                                        optional
                                        pending={act.isPending}
                                        onSubmit={(t) =>
                                            decide(a.stage, "approve", t)
                                        }
                                        className="h-10 bg-signal px-4 font-semibold text-ink hover:bg-signal/90 border-transparent"
                                    />
                                    <ReasonAction
                                        label="Send back"
                                        tone="danger"
                                        question="What does the rep need to fix?"
                                        submitLabel="Send back to the rep"
                                        pending={act.isPending}
                                        onSubmit={(t) =>
                                            decide(a.stage, "reject", t)
                                        }
                                    />
                                </div>
                            )}
                        </li>
                    );
                })}
            </ul>
            {earlier.length > 0 && (
                <details className="border-t px-4 py-3 text-sm">
                    <summary className="cursor-pointer text-muted-foreground">
                        Earlier rounds ({earlier.length})
                    </summary>
                    <ul className="mt-2 space-y-1">
                        {earlier.map((a, i) => (
                            <li key={`${a.round}-${a.stage}-${i}`}>
                                Round {a.round}, {stageLabel[a.stage]}:{" "}
                                {decisionText[a.decision]}
                                {a.comment ? `, “${a.comment}”` : ""}
                            </li>
                        ))}
                    </ul>
                </details>
            )}
            {act.isError && (
                <p
                    className="border-t px-4 py-3 text-sm font-medium text-late"
                    role="alert"
                >
                    {act.error.message}
                </p>
            )}
        </section>
    );
}

function DecisionIcon({ decision }: { decision: ApprovalDecision }) {
    const common = "size-5 rounded-full p-0.5";
    if (decision === "Approved")
        return (
            <LuCheck aria-hidden className={cn(common, "bg-ok text-white")} />
        );
    if (decision === "Rejected")
        return <LuX aria-hidden className={cn(common, "bg-late text-white")} />;
    if (decision === "Withdrawn")
        return (
            <LuMinus
                aria-hidden
                className={cn(common, "bg-muted text-muted-foreground")}
            />
        );
    return <LuClock aria-hidden className={cn(common, "bg-signal text-ink")} />;
}
