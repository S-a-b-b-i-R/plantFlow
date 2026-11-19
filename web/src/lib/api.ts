import { auth } from "./firebase";

export class ApiError extends Error {
    status: number;

    constructor(status: number, message: string) {
        super(message);
        this.status = status;
    }
}

// In development the Vite proxy forwards /api to the API. A deployed build can set VITE_API_URL.
const base = import.meta.env.VITE_API_URL ?? "";

// Calls the API with the signed-in user's Firebase token. Errors carry the API's problem title.
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
    const token = await auth.currentUser?.getIdToken();
    const headers = new Headers(init.headers);
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (
        init.body &&
        !(init.body instanceof FormData) &&
        !headers.has("Content-Type")
    ) {
        headers.set("Content-Type", "application/json");
    }

    const response = await fetch(`${base}${path}`, { ...init, headers });

    if (!response.ok) {
        let message = `The request failed (${response.status}).`;
        try {
            const body = await response.json();
            if (typeof body?.title === "string") message = body.title;
        } catch {
            // Not JSON: keep the generic message.
        }
        throw new ApiError(response.status, message);
    }

    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
}

// Downloads a file the API protects, such as a survey photo, as a blob.
export async function apiBlob(path: string): Promise<Blob> {
    const token = await auth.currentUser?.getIdToken();
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);

    const response = await fetch(`${base}${path}`, { headers });
    if (!response.ok) {
        throw new ApiError(
            response.status,
            `The file could not be loaded (${response.status}).`,
        );
    }
    return response.blob();
}
