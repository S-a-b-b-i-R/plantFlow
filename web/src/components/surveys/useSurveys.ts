import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiBlob } from "@/lib/api";
import type {
    Site,
    SiteSurvey,
    SurveyApprovals,
    SurveyRequest,
} from "@/lib/surveys";

// The surveyor work queue: open orders waiting for a new survey, soonest pour first.
export const useSurveyRequests = (enabled = true) =>
    useQuery({
        queryKey: ["surveys", "requests"],
        queryFn: () => api<SurveyRequest[]>("/api/survey-requests"),
        enabled,
        refetchInterval: 60_000,
    });

// Surveys waiting for approval at the plants the user approves for. Also feeds the badge in the menu.
export const useSurveyApprovals = (enabled = true) =>
    useQuery({
        queryKey: ["surveys", "approvals"],
        queryFn: () => api<SurveyApprovals>("/api/survey-approvals"),
        enabled,
        refetchInterval: 60_000,
    });

// Approves a survey, or sends it back with a reason (which reopens the order's request).
export function useReviewSurvey() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({
            surveyId,
            reason,
        }: {
            surveyId: string;
            reason?: string;
        }) =>
            reason === undefined
                ? api<SiteSurvey>(`/api/surveys/${surveyId}/approve`, {
                      method: "POST",
                  })
                : api<SiteSurvey>(`/api/surveys/${surveyId}/send-back`, {
                      method: "POST",
                      body: JSON.stringify({ reason }),
                  }),
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: ["surveys"] });
            void queryClient.invalidateQueries({ queryKey: ["orders"] });
        },
    });
}

export const useSurveyRequest = (orderId: string) =>
    useQuery({
        queryKey: ["surveys", "requests", orderId],
        queryFn: () => api<SurveyRequest>(`/api/survey-requests/${orderId}`),
        retry: false,
    });

export const useSite = (id: string) =>
    useQuery({
        queryKey: ["sites", "one", id],
        queryFn: () => api<Site>(`/api/sites/${id}`),
    });

export const useSiteSurveys = (siteId: string) =>
    useQuery({
        queryKey: ["surveys", "site", siteId],
        queryFn: () => api<SiteSurvey[]>(`/api/sites/${siteId}/surveys`),
        enabled: !!siteId,
    });

// A survey photo needs the sign-in token, so it is fetched and shown from an object URL.
export const useSurveyPhoto = (surveyId: string, imageId: string) =>
    useQuery({
        queryKey: ["surveys", "photo", imageId],
        queryFn: async () =>
            URL.createObjectURL(
                await apiBlob(`/api/surveys/${surveyId}/images/${imageId}`),
            ),
        staleTime: Infinity,
        gcTime: 10 * 60 * 1000,
    });

// Uploads one photo to a survey, then refreshes the surveys.
export function useAddPhoto(siteId: string) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({
            surveyId,
            file,
            caption,
        }: {
            surveyId: string;
            file: File;
            caption?: string;
        }) => uploadPhoto(surveyId, file, caption),
        onSettled: () =>
            queryClient.invalidateQueries({
                queryKey: ["surveys", "site", siteId],
            }),
    });
}

export function uploadPhoto(surveyId: string, file: File, caption?: string) {
    const form = new FormData();
    form.append("file", file);
    if (caption?.trim()) form.append("caption", caption.trim());
    return api<unknown>(`/api/surveys/${surveyId}/images`, {
        method: "POST",
        body: form,
    });
}

export function useRemovePhoto(siteId: string) {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({
            surveyId,
            imageId,
        }: {
            surveyId: string;
            imageId: string;
        }) =>
            api<unknown>(`/api/surveys/${surveyId}/images/${imageId}`, {
                method: "DELETE",
            }),
        onSettled: () =>
            queryClient.invalidateQueries({
                queryKey: ["surveys", "site", siteId],
            }),
    });
}
