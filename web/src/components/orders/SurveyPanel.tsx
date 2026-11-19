import { useState } from "react";
import { Link } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { OpButton as Button } from "@/components/operator/OpButton";
import { useSiteSurveys } from "@/components/surveys/useSurveys";
import { shortDate, type OrderSummary } from "@/lib/orders";
import { resultLabel } from "@/lib/surveys";
import { cn } from "@/lib/utils";
import { panelClass } from "./styles";
import { SurveyChoice, type SurveyPick } from "./SurveyChoice";
import { useOrderAct } from "./useOrderAct";

// Orders in these states can still change the survey they rely on (the API also checks planned pours).
const open = [
    "Draft",
    "Submitted",
    "InApproval",
    "Approved",
    "Confirmed",
    "OnHold",
];

// Which survey planning will check for this order, and a way to tag another or ask for a new one.
export function SurveyPanel({ order }: { order: OrderSummary }) {
    const me = useMe().data!;
    const canView = can(me, PERMISSIONS.siteSurveysView);
    const canChange =
        can(me, PERMISSIONS.ordersEdit) && open.includes(order.status);
    const surveys = useSiteSurveys(canView ? order.siteId : "");
    const tagged = surveys.data?.find((s) => s.id === order.surveyId);
    const [editing, setEditing] = useState(false);

    return (
        <section className={cn(panelClass, "space-y-3 p-4 text-sm")}>
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    <h2 className="display text-2xl">Site survey</h2>
                    <p className="mt-1">
                        {order.surveyRequested ? (
                            <span className="font-semibold">
                                Waiting for a surveyor to record a new survey.
                            </span>
                        ) : order.surveyId ? (
                            tagged ? (
                                <>
                                    <span
                                        className={cn(
                                            "font-semibold",
                                            tagged.result === "Ok"
                                                ? "text-ok"
                                                : "text-late",
                                        )}
                                    >
                                        {resultLabel[tagged.result]}
                                    </span>
                                    , surveyed {shortDate(tagged.surveyedOn)}
                                    {tagged.orderNumber &&
                                        tagged.orderNumber !== order.number &&
                                        ` for ${tagged.orderNumber}`}
                                    {tagged.status === "Pending" && (
                                        <span className="block font-semibold">
                                            Waiting for the plant manager's
                                            approval. Planning waits for it.
                                        </span>
                                    )}
                                </>
                            ) : (
                                "A survey is tagged."
                            )
                        ) : (
                            <span className="text-muted-foreground">
                                No survey. Pump jobs cannot be planned without
                                one.
                            </span>
                        )}
                    </p>
                    {canView && (
                        <Link
                            to={`/surveys/${order.siteId}`}
                            className="mt-1 inline-block text-muted-foreground underline underline-offset-2"
                        >
                            All surveys of {order.siteName}
                        </Link>
                    )}
                </div>
                {canChange && !editing && (
                    <Button
                        variant="outline"
                        className="h-11"
                        onClick={() => setEditing(true)}
                    >
                        Change survey
                    </Button>
                )}
            </div>

            {editing && (
                <SurveyEditor order={order} onDone={() => setEditing(false)} />
            )}
        </section>
    );
}

// Mounted fresh each time it opens, so it starts from the order's current choice.
function SurveyEditor({
    order,
    onDone,
}: {
    order: OrderSummary;
    onDone: () => void;
}) {
    const queryClient = useQueryClient();
    const act = useOrderAct();
    const [pick, setPick] = useState<SurveyPick>(() => ({
        surveyId: order.surveyId ?? "",
        requestSurvey: order.surveyRequested,
    }));

    const save = () =>
        act.mutate(
            {
                url: `/api/orders/${order.id}/survey`,
                method: "PUT",
                body: {
                    surveyId: pick.surveyId || null,
                    requestSurvey: pick.requestSurvey,
                },
            },
            {
                onSuccess: () => {
                    void queryClient.invalidateQueries({
                        queryKey: ["surveys"],
                    });
                    onDone();
                },
            },
        );

    return (
        <div className="space-y-3">
            <label className="block">
                <span className="mb-1 block font-medium">
                    Survey for this order
                </span>
                <SurveyChoice
                    siteId={order.siteId}
                    value={pick}
                    onChange={setPick}
                    disabled={act.isPending}
                />
            </label>
            {act.isError && (
                <p role="alert" className="text-late">
                    {act.error.message}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                {/* Not yellow: the order's own next step (confirm, approve) keeps the one yellow button. */}
                <Button
                    className="h-11"
                    disabled={act.isPending}
                    onClick={save}
                >
                    {act.isPending ? "Saving…" : "Save survey"}
                </Button>
                <Button
                    variant="outline"
                    className="h-11"
                    disabled={act.isPending}
                    onClick={onDone}
                >
                    Cancel
                </Button>
            </div>
        </div>
    );
}
