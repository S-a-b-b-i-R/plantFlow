// Shapes, wording and device helpers for site surveys. The field names are the API's.

export type SurveyResult = "Ok" | "NotOk";

export const resultLabel: Record<SurveyResult, string> = {
    Ok: "OK to pour",
    NotOk: "Not OK",
};

// A plant manager reviews every survey. Only an approved one counts for planning.
export type SurveyStatus = "Pending" | "Approved" | "Rejected";

export const statusLabel: Record<SurveyStatus, string> = {
    Pending: "Waiting for approval",
    Approved: "Approved",
    Rejected: "Sent back",
};

export interface SurveyImage {
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    caption: string | null;
}

export interface SiteSurvey {
    id: string;
    siteId: string;
    siteName: string;
    surveyedOn: string;
    result: SurveyResult;
    notes: string | null;
    surveyedBy: string | null;
    createdAtUtc: string;
    latitude: number | null;
    longitude: number | null;
    accuracyMeters: number | null;
    pipingMetres: number | null;
    pumpPosition: string | null;
    truckConnectionPoint: string | null;
    locationNote: string | null;
    images: SurveyImage[];
    // The order whose request it answered. Empty on surveys recorded before surveys came from orders.
    orderId: string | null;
    orderNumber: string | null;
    status: SurveyStatus;
    reviewedBy: string | null;
    reviewedAtUtc: string | null;
    // Why it was sent back.
    reviewComment: string | null;
}

// A survey waiting for a plant manager, with the order it was recorded for.
export interface SurveyApproval {
    survey: SiteSurvey;
    customerName: string;
    plantId: string;
    plantCode: string;
    requestedDate: string;
}

export interface SurveyApprovals {
    // False for an approver with no plants assigned.
    approvesForAnyPlant: boolean;
    surveys: SurveyApproval[];
}

// An order waiting for a new survey, as it shows in the surveyor work queue.
export interface SurveyRequest {
    orderId: string;
    orderNumber: string;
    orderStatus: string;
    customerName: string;
    siteId: string;
    siteName: string;
    siteAddress: string | null;
    siteLatitude: number | null;
    siteLongitude: number | null;
    siteAccessNotes: string | null;
    plantId: string;
    plantCode: string;
    requestedDate: string;
    surveyRequestedAtUtc: string | null;
    hasPumpLines: boolean;
    // Why the last survey for this order was sent back, if it was.
    sentBackReason: string | null;
}

// An order can tag one of its site's last this many approved surveys.
export const TAGGABLE_SURVEYS = 3;

export interface Site {
    id: string;
    customerId: string;
    name: string;
    address: string | null;
    latitude: number | null;
    longitude: number | null;
    contactName: string | null;
    contactPhone: string | null;
    accessNotes: string | null;
    status: "Active" | "Inactive";
}

export const MAX_PHOTOS = 3;

// The API accepts at most 6 decimals for a position and 1 for metres.
export const round = (n: number, decimals: number) =>
    Number(n.toFixed(decimals));

export const mapLink = (latitude: number, longitude: number) =>
    `https://www.google.com/maps?q=${latitude},${longitude}`;

// A position is good enough to find the gate when it is within this many metres.
export const GOOD_ACCURACY_METERS = 30;

export type Position = {
    latitude: number;
    longitude: number;
    accuracyMeters: number;
};

// Reads the device's position once, as accurately as the phone can.
export function readPosition(): Promise<Position> {
    return new Promise((resolve, reject) => {
        if (!("geolocation" in navigator)) {
            reject(new Error("This device cannot give its position."));
            return;
        }
        navigator.geolocation.getCurrentPosition(
            (p) =>
                resolve({
                    latitude: round(p.coords.latitude, 6),
                    longitude: round(p.coords.longitude, 6),
                    accuracyMeters: round(p.coords.accuracy, 1),
                }),
            (error) =>
                reject(
                    new Error(
                        error.code === error.PERMISSION_DENIED
                            ? "Location is turned off for this site. Allow location for PlantFlow in the browser settings, then try again."
                            : error.code === error.TIMEOUT
                              ? "The position took too long. Step outside, away from buildings, and try again."
                              : "The position could not be read. Check that location is turned on.",
                    ),
                ),
            { enableHighAccuracy: true, timeout: 25000, maximumAge: 0 },
        );
    });
}

const MAX_SIDE = 1600;
const MAX_BYTES = 5 * 1024 * 1024;

// Shrinks a phone photo to a JPEG that fits the 5 MB limit. A phone photo is often 4 to 8 MB.
export async function preparePhoto(file: File): Promise<File> {
    let bitmap: ImageBitmap;
    try {
        bitmap = await createImageBitmap(file);
    } catch {
        throw new Error(
            "This photo could not be read. Use a JPEG, PNG or WebP photo.",
        );
    }

    const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);
    canvas
        .getContext("2d")!
        .drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();

    const blob = await new Promise<Blob | null>((resolve) =>
        canvas.toBlob(resolve, "image/jpeg", 0.82),
    );
    if (!blob || blob.size > MAX_BYTES) {
        throw new Error(
            "This photo is too large. Take it again at a lower size.",
        );
    }

    const name = file.name.replace(/\.[^.]+$/, "") || "photo";
    return new File([blob], `${name}.jpg`, { type: "image/jpeg" });
}
