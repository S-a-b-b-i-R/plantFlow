import { useRef, useState } from "react";
import { LuCamera, LuMapPin, LuX } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { panelClass } from "@/components/orders/styles";
import { shortDate } from "@/lib/orders";
import {
    mapLink,
    MAX_PHOTOS,
    preparePhoto,
    resultLabel,
    type SiteSurvey,
    type SurveyImage,
} from "@/lib/surveys";
import { cn } from "@/lib/utils";
import { useAddPhoto, useRemovePhoto, useSurveyPhoto } from "./useSurveys";

function Fact({ label, value }: { label: string; value: string | null }) {
    if (!value) return null;
    return (
        <div>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-sm whitespace-pre-line">{value}</dd>
        </div>
    );
}

function Photo({
    surveyId,
    image,
    onRemove,
}: {
    surveyId: string;
    image: SurveyImage;
    onRemove?: () => void;
}) {
    const photo = useSurveyPhoto(surveyId, image.id);
    return (
        <figure className="relative">
            {photo.data ? (
                <a href={photo.data} target="_blank" rel="noreferrer">
                    <img
                        src={photo.data}
                        alt={image.caption ?? "Site photo"}
                        className="aspect-4/3 w-full rounded-sm border object-cover"
                    />
                </a>
            ) : (
                <div className="aspect-4/3 w-full rounded-sm border bg-muted" />
            )}
            {image.caption && (
                <figcaption className="mt-1 text-xs text-muted-foreground">
                    {image.caption}
                </figcaption>
            )}
            {onRemove && (
                <button
                    type="button"
                    onClick={onRemove}
                    aria-label="Remove this photo"
                    className="absolute top-1 right-1 grid size-8 place-items-center rounded-full bg-ink/80 text-white"
                >
                    <LuX aria-hidden className="size-4" />
                </button>
            )}
        </figure>
    );
}

// One survey: the verdict, where it was, how to set up the pump, and the photos.
export function SurveyCard({
    survey,
    latest,
    canAttach,
}: {
    survey: SiteSurvey;
    latest: boolean;
    canAttach: boolean;
}) {
    const add = useAddPhoto(survey.siteId);
    const remove = useRemovePhoto(survey.siteId);
    const input = useRef<HTMLInputElement>(null);
    const [error, setError] = useState<string | null>(null);

    const ok = survey.result === "Ok";
    const pumpFacts =
        survey.pipingMetres !== null ||
        survey.pumpPosition ||
        survey.truckConnectionPoint;

    const pick = async (file: File | undefined) => {
        if (!file) return;
        setError(null);
        try {
            add.mutate(
                { surveyId: survey.id, file: await preparePhoto(file) },
                { onError: (e) => setError(e.message) },
            );
        } catch (e) {
            setError((e as Error).message);
        }
    };

    return (
        <article className={cn(panelClass, "space-y-3 p-4")}>
            <header className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                    <span
                        className={cn(
                            "inline-flex items-center rounded-sm border px-2 py-0.5 text-sm font-semibold",
                            ok
                                ? "border-ok/40 bg-ok/10 text-ok"
                                : "border-late/40 bg-late/10 text-late",
                        )}
                    >
                        {resultLabel[survey.result]}
                    </span>
                    {latest && (
                        <span className="text-xs text-muted-foreground">
                            Latest. Planning uses this one.
                        </span>
                    )}
                </div>
                <span className="text-sm text-muted-foreground">
                    {shortDate(survey.surveyedOn)}
                </span>
            </header>

            {survey.latitude !== null && survey.longitude !== null && (
                <a
                    href={mapLink(survey.latitude, survey.longitude)}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-2 text-sm underline underline-offset-2"
                >
                    <LuMapPin aria-hidden className="size-4" />
                    {survey.latitude}, {survey.longitude}
                    {survey.accuracyMeters !== null &&
                        ` (within ${survey.accuracyMeters} m)`}
                </a>
            )}

            <dl className="space-y-2">
                <Fact label="How to find it" value={survey.locationNote} />
                {pumpFacts && (
                    <>
                        <Fact
                            label="Piping needed"
                            value={
                                survey.pipingMetres !== null
                                    ? `${survey.pipingMetres} m`
                                    : null
                            }
                        />
                        <Fact
                            label="Pump position"
                            value={survey.pumpPosition}
                        />
                        <Fact
                            label="Truck connection point"
                            value={survey.truckConnectionPoint}
                        />
                    </>
                )}
                <Fact label="Notes" value={survey.notes} />
            </dl>

            {(survey.images.length > 0 || canAttach) && (
                <div>
                    {survey.images.length > 0 && (
                        <div className="grid grid-cols-3 gap-2">
                            {survey.images.map((image) => (
                                <Photo
                                    key={image.id}
                                    surveyId={survey.id}
                                    image={image}
                                    onRemove={
                                        canAttach
                                            ? () =>
                                                  remove.mutate({
                                                      surveyId: survey.id,
                                                      imageId: image.id,
                                                  })
                                            : undefined
                                    }
                                />
                            ))}
                        </div>
                    )}
                    {canAttach && survey.images.length < MAX_PHOTOS && (
                        <>
                            <input
                                ref={input}
                                type="file"
                                accept="image/*"
                                capture="environment"
                                className="sr-only"
                                tabIndex={-1}
                                onChange={(e) => {
                                    pick(e.target.files?.[0]);
                                    e.target.value = "";
                                }}
                            />
                            <Button
                                variant="outline"
                                className="mt-2 h-11 px-4"
                                disabled={add.isPending}
                                onClick={() => input.current?.click()}
                            >
                                <LuCamera aria-hidden className="size-4" />
                                {add.isPending ? "Uploading…" : "Add a photo"}
                            </Button>
                        </>
                    )}
                    {error && (
                        <p role="alert" className="mt-2 text-sm text-late">
                            {error}
                        </p>
                    )}
                </div>
            )}
        </article>
    );
}
