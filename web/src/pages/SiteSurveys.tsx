import { Link, useParams } from "react-router-dom";
import { LuChevronLeft, LuPlus } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { nextButtonClass } from "@/components/orders/styles";
import { SurveyCard } from "@/components/surveys/SurveyCard";
import { useSite, useSiteSurveys } from "@/components/surveys/useSurveys";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

// Everything known about one site: how to reach it and set up there, newest survey first.
export function SiteSurveys() {
    const me = useMe().data!;
    const { siteId } = useParams();
    const site = useSite(siteId!);
    const surveys = useSiteSurveys(siteId!);

    const canPerform = can(me, PERMISSIONS.siteSurveysPerform);
    const canAttach = can(me, PERMISSIONS.siteSurveysAttach);

    const list = surveys.data ?? [];
    const latestOk = list.length > 0 && list[0].result === "Ok";

    return (
        <div className="mx-auto max-w-2xl space-y-4">
            <Link
                to={
                    site.data
                        ? `/surveys?customer=${site.data.customerId}`
                        : "/surveys"
                }
                className="inline-flex h-10 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
            >
                <LuChevronLeft aria-hidden className="size-4" />
                Sites
            </Link>

            {site.isLoading && <Skeleton className="h-20 w-full" />}
            {site.isError && (
                <p role="alert" className="text-sm text-late">
                    {site.error.message}
                </p>
            )}
            {site.data && (
                <header>
                    <h1 className="display text-4xl leading-none">
                        {site.data.name}
                    </h1>
                    {site.data.address && (
                        <p className="mt-2 text-sm text-muted-foreground">
                            {site.data.address}
                        </p>
                    )}
                    {site.data.accessNotes && (
                        <p className="mt-1 text-sm">{site.data.accessNotes}</p>
                    )}
                    {(site.data.contactName || site.data.contactPhone) && (
                        <p className="mt-1 text-sm text-muted-foreground">
                            Contact: {site.data.contactName}{" "}
                            {site.data.contactPhone && (
                                <a
                                    href={`tel:${site.data.contactPhone}`}
                                    className="underline underline-offset-2"
                                >
                                    {site.data.contactPhone}
                                </a>
                            )}
                        </p>
                    )}
                </header>
            )}

            {canPerform && (
                <Link
                    to={`/surveys/${siteId}/new`}
                    className={cn(
                        "inline-flex h-12 w-full items-center justify-center gap-2 rounded-sm px-6 text-base sm:w-auto",
                        // Yellow only when a new survey is the next thing to do.
                        list.length === 0 || !latestOk
                            ? nextButtonClass
                            : "border border-ink/30 bg-card font-medium hover:border-ink hover:bg-ink hover:text-white",
                    )}
                >
                    <LuPlus aria-hidden className="size-5" />
                    New survey
                </Link>
            )}

            {surveys.isLoading && <Skeleton className="h-40 w-full" />}
            {surveys.isError && (
                <p role="alert" className="text-sm text-late">
                    {surveys.error.message}
                </p>
            )}
            {surveys.data && list.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    This site has not been surveyed yet. A pumped pour cannot be
                    planned until a survey with an OK result is recorded.
                </p>
            )}

            <div className="space-y-3">
                {list.map((survey, index) => (
                    <SurveyCard
                        key={survey.id}
                        survey={survey}
                        latest={index === 0}
                        canAttach={canAttach}
                    />
                ))}
            </div>
        </div>
    );
}
