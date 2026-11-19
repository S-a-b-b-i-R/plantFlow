import { Link, useParams } from "react-router-dom";
import { LuChevronLeft } from "react-icons/lu";
import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { SurveyCard } from "@/components/surveys/SurveyCard";
import { useSite, useSiteSurveys } from "@/components/surveys/useSurveys";
import { Skeleton } from "@/components/ui/skeleton";

// Everything known about one site: how to reach it and set up there, newest survey first.
// New surveys are recorded from an order's request, in the Surveys work queue.
export function SiteSurveys() {
    const me = useMe().data!;
    const { siteId } = useParams();
    const site = useSite(siteId!);
    const surveys = useSiteSurveys(siteId!);

    const canAttach = can(me, PERMISSIONS.siteSurveysAttach);

    const list = surveys.data ?? [];

    return (
        <div className="mx-auto max-w-2xl space-y-4">
            <Link
                to="/surveys"
                className="inline-flex h-10 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
            >
                <LuChevronLeft aria-hidden className="size-4" />
                Survey requests
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

            {surveys.isLoading && <Skeleton className="h-40 w-full" />}
            {surveys.isError && (
                <p role="alert" className="text-sm text-late">
                    {surveys.error.message}
                </p>
            )}
            {surveys.data && list.length === 0 && (
                <p className="text-sm text-muted-foreground">
                    This site has not been surveyed yet. An order for it can
                    request a survey, and a surveyor records it from the
                    Surveys work queue.
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
