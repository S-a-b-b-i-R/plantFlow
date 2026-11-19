import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
    LuCamera,
    LuChevronLeft,
    LuImage,
    LuMapPin,
    LuX,
} from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
    textareaClass,
} from "@/components/orders/styles";
import {
    uploadPhoto,
    useSurveyRequest,
} from "@/components/surveys/useSurveys";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError, api } from "@/lib/api";
import { shortDate } from "@/lib/orders";
import { todayIso } from "@/lib/planning";
import {
    GOOD_ACCURACY_METERS,
    MAX_PHOTOS,
    preparePhoto,
    readPosition,
    resultLabel,
    type Position,
    type SiteSurvey,
    type SurveyResult,
} from "@/lib/surveys";
import { cn } from "@/lib/utils";

type Photo = {
    key: string;
    file: File;
    preview: string;
    caption: string;
    uploaded: boolean;
};

const choices: SurveyResult[] = ["Ok", "NotOk"];

// A survey for an order that asked for one, filled in on the phone at the site: the verdict, the position,
// the pump setup and up to three photos. Saving it answers the request and tags it on the order.
export function SurveyForm() {
    const { orderId } = useParams();
    const navigate = useNavigate();
    const queryClient = useQueryClient();
    const request = useSurveyRequest(orderId!);
    const siteId = request.data?.siteId;

    const [result, setResult] = useState<SurveyResult | null>(null);
    const [date, setDate] = useState(todayIso);
    const [position, setPosition] = useState<Position | null>(null);
    const [locating, setLocating] = useState(false);
    const [locationError, setLocationError] = useState<string | null>(null);
    const [locationNote, setLocationNote] = useState("");
    const [piping, setPiping] = useState("");
    const [pumpPosition, setPumpPosition] = useState("");
    const [truckPoint, setTruckPoint] = useState("");
    const [notes, setNotes] = useState("");
    const [photos, setPhotos] = useState<Photo[]>([]);
    const [photoError, setPhotoError] = useState<string | null>(null);
    // Once the survey itself is saved, a retry only uploads the photos that are missing.
    const [savedId, setSavedId] = useState<string | null>(null);

    const camera = useRef<HTMLInputElement>(null);
    const gallery = useRef<HTMLInputElement>(null);
    const previews = useRef<string[]>([]);
    useEffect(
        () => () => previews.current.forEach((url) => URL.revokeObjectURL(url)),
        [],
    );

    const locate = async () => {
        setLocating(true);
        setLocationError(null);
        try {
            setPosition(await readPosition());
        } catch (e) {
            setLocationError((e as Error).message);
        } finally {
            setLocating(false);
        }
    };

    const addPhotos = async (files: FileList | null) => {
        if (!files) return;
        setPhotoError(null);
        for (const file of Array.from(files).slice(
            0,
            MAX_PHOTOS - photos.length,
        )) {
            try {
                const prepared = await preparePhoto(file);
                const preview = URL.createObjectURL(prepared);
                previews.current.push(preview);
                setPhotos((current) =>
                    current.length >= MAX_PHOTOS
                        ? current
                        : [
                              ...current,
                              {
                                  key: crypto.randomUUID(),
                                  file: prepared,
                                  preview,
                                  caption: "",
                                  uploaded: false,
                              },
                          ],
                );
            } catch (e) {
                setPhotoError((e as Error).message);
            }
        }
    };

    const pipingOk =
        piping.trim() === "" ||
        (Number(piping) >= 0 &&
            Number(piping) <= 10000 &&
            Math.round(Number(piping) * 10) / 10 === Number(piping));
    const ready =
        !!request.data &&
        result !== null &&
        position !== null &&
        !!date &&
        pipingOk;

    const save = useMutation({
        mutationFn: async () => {
            let id = savedId;
            if (!id) {
                const created = await api<SiteSurvey>(
                    `/api/orders/${orderId}/surveys`,
                    {
                        method: "POST",
                        body: JSON.stringify({
                            surveyedOn: date,
                            result,
                            latitude: position!.latitude,
                            longitude: position!.longitude,
                            accuracyMeters: position!.accuracyMeters,
                            pipingMetres:
                                piping.trim() === "" ? null : Number(piping),
                            pumpPosition: pumpPosition.trim() || null,
                            truckConnectionPoint: truckPoint.trim() || null,
                            locationNote: locationNote.trim() || null,
                            notes: notes.trim() || null,
                        }),
                    },
                );
                id = created.id;
                setSavedId(id);
            }

            for (const photo of photos.filter((p) => !p.uploaded)) {
                await uploadPhoto(id, photo.file, photo.caption);
                setPhotos((current) =>
                    current.map((p) =>
                        p.key === photo.key ? { ...p, uploaded: true } : p,
                    ),
                );
            }
        },
        onSuccess: () => navigate(siteId ? `/surveys/${siteId}` : "/surveys"),
        onSettled: () =>
            queryClient.invalidateQueries({ queryKey: ["surveys"] }),
    });

    const locked = savedId !== null;
    const unsent = photos.filter((p) => !p.uploaded).length;

    return (
        <form
            onSubmit={(e) => {
                e.preventDefault();
                save.mutate();
            }}
            className="mx-auto max-w-2xl space-y-6 pb-4"
        >
            <Link
                to="/surveys"
                className="inline-flex h-10 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
            >
                <LuChevronLeft aria-hidden className="size-4" />
                Survey requests
            </Link>

            <header>
                <h1 className="display text-4xl leading-none">New survey</h1>
                {request.isLoading && <Skeleton className="mt-2 h-10 w-full" />}
                {request.isError && (
                    <p role="alert" className="mt-2 text-sm text-late">
                        {request.error instanceof ApiError &&
                        request.error.status === 404
                            ? "This order is not waiting for a survey, or it is for a plant you do not work for."
                            : request.error.message}
                    </p>
                )}
                {request.data && (
                    <>
                        <p className="mt-2 font-semibold">
                            {request.data.siteName}
                        </p>
                        <p className="text-sm text-muted-foreground">
                            {request.data.customerName}
                            {request.data.siteAddress
                                ? ` · ${request.data.siteAddress}`
                                : ""}
                        </p>
                        <p className="mt-1 text-xs text-muted-foreground">
                            For {request.data.orderNumber} ·{" "}
                            {request.data.plantCode} · pour{" "}
                            {shortDate(request.data.requestedDate)}
                        </p>
                        {request.data.siteAccessNotes && (
                            <p className="mt-1 text-sm">
                                {request.data.siteAccessNotes}
                            </p>
                        )}
                    </>
                )}
            </header>

            <fieldset disabled={locked} className="space-y-6">
                <section className="space-y-2">
                    <h2 className="text-sm font-medium">
                        Can concrete be poured here?
                    </h2>
                    <div className="grid grid-cols-2 gap-3" role="radiogroup">
                        {choices.map((choice) => (
                            <button
                                key={choice}
                                type="button"
                                role="radio"
                                aria-checked={result === choice}
                                onClick={() => setResult(choice)}
                                className={cn(
                                    "h-14 rounded-sm border text-base font-semibold",
                                    result === choice
                                        ? choice === "Ok"
                                            ? "border-ok bg-ok text-white"
                                            : "border-late bg-late text-white"
                                        : "bg-card hover:border-ink",
                                )}
                            >
                                {resultLabel[choice]}
                            </button>
                        ))}
                    </div>
                </section>

                <section className={cn(panelClass, "space-y-3 p-4")}>
                    <h2 className="text-sm font-medium">Position</h2>
                    {position ? (
                        <p className="text-sm">
                            <span className="font-semibold">
                                {position.latitude}, {position.longitude}
                            </span>
                            <br />
                            <span
                                className={cn(
                                    position.accuracyMeters >
                                        GOOD_ACCURACY_METERS && "text-late",
                                )}
                            >
                                Within {position.accuracyMeters} m
                                {position.accuracyMeters >
                                    GOOD_ACCURACY_METERS &&
                                    ". That is rough. Step into the open and read it again."}
                            </span>
                        </p>
                    ) : (
                        <p className="text-sm text-muted-foreground">
                            Stand at the place where the pump or truck will be,
                            then read your position.
                        </p>
                    )}
                    <Button
                        type="button"
                        variant={position ? "outline" : undefined}
                        className={cn(
                            "h-12 w-full px-5 sm:w-auto",
                            !position && result !== null && nextButtonClass,
                        )}
                        disabled={locating}
                        onClick={locate}
                    >
                        <LuMapPin aria-hidden className="size-4" />
                        {locating
                            ? "Reading position…"
                            : position
                              ? "Read it again"
                              : "Use my location"}
                    </Button>
                    {locationError && (
                        <p role="alert" className="text-sm text-late">
                            {locationError}
                        </p>
                    )}
                </section>

                <Field
                    label="How to find the place"
                    hint="Landmarks, which gate, what to tell the driver."
                >
                    <textarea
                        rows={3}
                        maxLength={1000}
                        className={textareaClass}
                        value={locationNote}
                        onChange={(e) => setLocationNote(e.target.value)}
                    />
                </Field>

                <section className="space-y-4">
                    <h2 className="display text-2xl leading-none">
                        Pump setup
                    </h2>
                    <Field
                        label="Piping needed (metres)"
                        hint={
                            pipingOk
                                ? undefined
                                : "Use a number from 0 to 10000 with at most 1 decimal."
                        }
                    >
                        <input
                            inputMode="decimal"
                            className={cn(inputClass, "h-12")}
                            value={piping}
                            onChange={(e) => setPiping(e.target.value)}
                        />
                    </Field>
                    <Field label="Where the pump goes">
                        <textarea
                            rows={2}
                            maxLength={500}
                            className={textareaClass}
                            value={pumpPosition}
                            onChange={(e) => setPumpPosition(e.target.value)}
                        />
                    </Field>
                    <Field label="Where the trucks connect">
                        <textarea
                            rows={2}
                            maxLength={500}
                            className={textareaClass}
                            value={truckPoint}
                            onChange={(e) => setTruckPoint(e.target.value)}
                        />
                    </Field>
                </section>

                <Field label="Anything else">
                    <textarea
                        rows={3}
                        maxLength={1000}
                        className={textareaClass}
                        value={notes}
                        onChange={(e) => setNotes(e.target.value)}
                    />
                </Field>

                <Field label="Survey date">
                    <input
                        type="date"
                        className={cn(inputClass, "h-12")}
                        value={date}
                        onChange={(e) => setDate(e.target.value)}
                    />
                </Field>
            </fieldset>

            <section className="space-y-3">
                <h2 className="text-sm font-medium">
                    Photos ({photos.length} of {MAX_PHOTOS})
                </h2>
                {photos.length > 0 && (
                    <ul className="grid grid-cols-3 gap-2">
                        {photos.map((photo) => (
                            <li key={photo.key} className="space-y-1">
                                <div className="relative">
                                    <img
                                        src={photo.preview}
                                        alt="Survey photo"
                                        className="aspect-4/3 w-full rounded-sm border object-cover"
                                    />
                                    {!photo.uploaded && (
                                        <button
                                            type="button"
                                            aria-label="Remove this photo"
                                            onClick={() =>
                                                setPhotos((current) =>
                                                    current.filter(
                                                        (p) =>
                                                            p.key !== photo.key,
                                                    ),
                                                )
                                            }
                                            className="absolute top-1 right-1 grid size-8 place-items-center rounded-full bg-ink/80 text-white"
                                        >
                                            <LuX
                                                aria-hidden
                                                className="size-4"
                                            />
                                        </button>
                                    )}
                                </div>
                                {photo.uploaded ? (
                                    <p className="text-xs text-ok">Uploaded</p>
                                ) : (
                                    <input
                                        aria-label="Photo caption"
                                        placeholder="Caption"
                                        maxLength={200}
                                        className={cn(
                                            inputClass,
                                            "h-9 text-xs",
                                        )}
                                        value={photo.caption}
                                        onChange={(e) =>
                                            setPhotos((current) =>
                                                current.map((p) =>
                                                    p.key === photo.key
                                                        ? {
                                                              ...p,
                                                              caption:
                                                                  e.target
                                                                      .value,
                                                          }
                                                        : p,
                                                ),
                                            )
                                        }
                                    />
                                )}
                            </li>
                        ))}
                    </ul>
                )}
                {photos.length < MAX_PHOTOS && (
                    <div className="flex flex-wrap gap-2">
                        <input
                            ref={camera}
                            type="file"
                            accept="image/*"
                            capture="environment"
                            className="sr-only"
                            tabIndex={-1}
                            onChange={(e) => {
                                addPhotos(e.target.files);
                                e.target.value = "";
                            }}
                        />
                        <input
                            ref={gallery}
                            type="file"
                            accept="image/*"
                            multiple
                            className="sr-only"
                            tabIndex={-1}
                            onChange={(e) => {
                                addPhotos(e.target.files);
                                e.target.value = "";
                            }}
                        />
                        <Button
                            type="button"
                            variant="outline"
                            className="h-12 flex-1 px-4 sm:flex-none"
                            onClick={() => camera.current?.click()}
                        >
                            <LuCamera aria-hidden className="size-4" />
                            Take a photo
                        </Button>
                        <Button
                            type="button"
                            variant="outline"
                            className="h-12 flex-1 px-4 sm:flex-none"
                            onClick={() => gallery.current?.click()}
                        >
                            <LuImage aria-hidden className="size-4" />
                            Choose photos
                        </Button>
                    </div>
                )}
                {photoError && (
                    <p role="alert" className="text-sm text-late">
                        {photoError}
                    </p>
                )}
            </section>

            <div className="sticky bottom-0 -mx-4 space-y-2 border-t bg-background px-4 py-3 sm:mx-0 sm:px-0">
                {save.isError && (
                    <p role="alert" className="text-sm text-late">
                        {locked
                            ? `The survey is saved, but ${unsent} photo${unsent === 1 ? "" : "s"} did not upload: ${save.error.message} Try again.`
                            : save.error.message}
                    </p>
                )}
                {!ready && !locked && (
                    <p className="text-xs text-muted-foreground">
                        {result === null ? "Choose OK or Not OK. " : ""}
                        {position === null ? "Read your position. " : ""}
                        {!pipingOk ? "Check the piping length." : ""}
                    </p>
                )}
                <Button
                    type="submit"
                    className={cn(
                        "h-12 w-full px-6 text-base",
                        nextButtonClass,
                    )}
                    disabled={!ready || save.isPending}
                >
                    {save.isPending
                        ? "Saving…"
                        : locked
                          ? "Upload the photos again"
                          : "Save survey"}
                </Button>
            </div>
        </form>
    );
}
