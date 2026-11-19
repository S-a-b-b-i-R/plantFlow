import type { UseQueryResult } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { ReasonAction } from "@/components/orders/ReasonAction";
import { panelClass } from "@/components/orders/styles";
import { Skeleton } from "@/components/ui/skeleton";
import { shortDate } from "@/lib/orders";
import type { SurveyApproval, SurveyApprovals } from "@/lib/surveys";
import { cn } from "@/lib/utils";
import { SurveyCard } from "./SurveyCard";
import { useReviewSurvey } from "./useSurveys";

// Recorded surveys waiting for a plant manager of the order's plant, oldest first.
export function ApprovalQueue({
    approvals,
}: {
    approvals: UseQueryResult<SurveyApprovals, Error>;
}) {
    return (
        <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
                Check each survey before planning relies on it. Approve it, or
                send it back with a reason and the order goes back to the
                surveyors.
            </p>

            {approvals.isLoading && <Skeleton className="h-32 w-full" />}
            {approvals.isError && (
                <p role="alert" className="text-sm text-late">
                    {approvals.error.message}
                </p>
            )}
            {approvals.data && !approvals.data.approvesForAnyPlant && (
                <p className={cn(panelClass, "p-4 text-sm")}>
                    You approve surveys for the plants you work for, and no
                    plant is assigned to you yet. Ask an admin to assign your
                    plants on the Users screen.
                </p>
            )}
            {approvals.data?.approvesForAnyPlant &&
                approvals.data.surveys.length === 0 && (
                    <p
                        className={cn(
                            panelClass,
                            "p-4 text-sm text-muted-foreground",
                        )}
                    >
                        No survey is waiting for approval.
                    </p>
                )}

            <ul className="space-y-4">
                {approvals.data?.surveys.map((item) => (
                    <li key={item.survey.id}>
                        <Pending item={item} />
                    </li>
                ))}
            </ul>
        </div>
    );
}

function Pending({ item }: { item: SurveyApproval }) {
    const me = useMe().data!;
    const review = useReviewSurvey();
    const { survey } = item;

    return (
        <div className="space-y-2">
            <p className="text-sm">
                <span className="font-semibold">{survey.siteName}</span> ·{" "}
                {item.customerName}
                <span className="block text-xs text-muted-foreground">
                    {survey.orderId ? (
                        <Link
                            to={`/orders/${survey.orderId}`}
                            className="underline underline-offset-2"
                        >
                            {survey.orderNumber}
                        </Link>
                    ) : (
                        survey.orderNumber
                    )}{" "}
                    · {item.plantCode} · pour {shortDate(item.requestedDate)}
                </span>
            </p>
            <SurveyCard
                survey={survey}
                latest={false}
                canAttach={can(me, PERMISSIONS.siteSurveysAttach)}
                actions={
                    <div className="space-y-2 border-t pt-3">
                        <div className="flex flex-wrap gap-2">
                            {/* Dark, not yellow: a list of equal items has no single next action. */}
                            <Button
                                className="h-11 px-5"
                                disabled={review.isPending}
                                onClick={() =>
                                    review.mutate({ surveyId: survey.id })
                                }
                            >
                                Approve
                            </Button>
                            <ReasonAction
                                label="Send back"
                                question="What needs to be surveyed again?"
                                submitLabel="Send back"
                                tone="danger"
                                className="h-11 px-4"
                                pending={review.isPending}
                                onSubmit={(reason) =>
                                    review.mutate({
                                        surveyId: survey.id,
                                        reason,
                                    })
                                }
                            />
                        </div>
                        {review.isError && (
                            <p role="alert" className="text-sm text-late">
                                {review.error.message}
                            </p>
                        )}
                    </div>
                }
            />
        </div>
    );
}
