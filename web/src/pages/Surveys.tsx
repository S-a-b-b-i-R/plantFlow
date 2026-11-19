import { Link } from "react-router-dom";
import { LuChevronRight, LuMapPin } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { panelClass } from "@/components/orders/styles";
import { useSurveyRequests } from "@/components/surveys/useSurveys";
import { Skeleton } from "@/components/ui/skeleton";
import { shortDate } from "@/lib/orders";
import { mapLink } from "@/lib/surveys";
import { cn } from "@/lib/utils";

// The surveyor work queue: orders that asked for a new site survey, soonest pour first.
// Only the plants the surveyor works for are listed (all plants when none are assigned).
export function Surveys() {
    const me = useMe().data!;
    const canPerform = can(me, PERMISSIONS.siteSurveysPerform);
    const requests = useSurveyRequests(canPerform);

    return (
        <div className="mx-auto max-w-2xl space-y-4">
            <header>
                <h1 className="display text-4xl leading-none">Site surveys</h1>
                <p className="mt-2 text-sm text-muted-foreground">
                    Orders waiting for a survey. Go to the site, then record
                    what you see there: where it is, how to reach it and where
                    the pump goes.
                </p>
            </header>

            {!canPerform && (
                <p className="text-sm text-muted-foreground">
                    Surveys are recorded by the survey team. To see a site's
                    surveys, open one of its orders.
                </p>
            )}

            {requests.isLoading && <Skeleton className="h-32 w-full" />}
            {requests.isError && (
                <p role="alert" className="text-sm text-late">
                    {requests.error.message}
                </p>
            )}
            {requests.data && requests.data.length === 0 && (
                <p
                    className={cn(
                        panelClass,
                        "p-4 text-sm text-muted-foreground",
                    )}
                >
                    No survey is waiting. New requests show up here when an
                    order asks for one.
                </p>
            )}

            <ul className="space-y-3">
                {requests.data?.map((request) => (
                    <li key={request.orderId} className={panelClass}>
                        <Link
                            to={`/surveys/requests/${request.orderId}`}
                            className="flex min-h-16 items-center justify-between gap-3 px-4 py-3 hover:bg-muted/50"
                        >
                            <span className="min-w-0">
                                <span className="block font-semibold">
                                    {request.siteName}
                                </span>
                                <span className="block text-sm">
                                    {request.customerName}
                                </span>
                                {request.siteAddress && (
                                    <span className="block truncate text-sm text-muted-foreground">
                                        {request.siteAddress}
                                    </span>
                                )}
                                <span className="mt-1 block text-xs text-muted-foreground">
                                    {request.orderNumber} · {request.plantCode}{" "}
                                    · pour {shortDate(request.requestedDate)}
                                    {request.hasPumpLines && " · pump job"}
                                </span>
                            </span>
                            <LuChevronRight
                                aria-hidden
                                className="size-5 shrink-0 text-muted-foreground"
                            />
                        </Link>
                        {request.siteLatitude !== null &&
                            request.siteLongitude !== null && (
                                <a
                                    href={mapLink(
                                        request.siteLatitude,
                                        request.siteLongitude,
                                    )}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="flex min-h-11 items-center gap-2 border-t px-4 text-sm underline-offset-2 hover:underline"
                                >
                                    <LuMapPin aria-hidden className="size-4" />
                                    Open the site on the map
                                </a>
                            )}
                    </li>
                ))}
            </ul>
        </div>
    );
}
