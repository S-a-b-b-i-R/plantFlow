import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiBlob } from "@/lib/api";
import type { CustomerOption, Paged, SiteOption } from "@/lib/orders";
import type { Site, SiteSurvey } from "@/lib/surveys";

export const useCustomerSearch = (search: string, enabled: boolean) =>
    useQuery({
        queryKey: ["customers", "pick", search],
        queryFn: () =>
            api<Paged<CustomerOption>>(
                `/api/customers?status=Active&pageSize=8&search=${encodeURIComponent(search)}`,
            ),
        enabled,
    });

export const useCustomer = (id: string | null) =>
    useQuery({
        queryKey: ["customers", id],
        queryFn: () => api<CustomerOption>(`/api/customers/${id}`),
        enabled: !!id,
    });

export const useSites = (customerId: string | null) =>
    useQuery({
        queryKey: ["sites", customerId],
        queryFn: () =>
            api<Paged<SiteOption>>(
                `/api/customers/${customerId}/sites?status=Active&pageSize=100`,
            ),
        enabled: !!customerId,
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
