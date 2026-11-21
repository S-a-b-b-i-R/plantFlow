import { useMutation, useQuery } from "@tanstack/react-query";
import { api, apiBlob } from "@/lib/api";
import {
    auditParams,
    type AuditEntry,
    type AuditFilters,
    type AuditQuery,
} from "@/lib/audit";
import type { Paged } from "@/lib/orders";

export const PAGE_SIZE = 50;

export const useAuditLog = (query: AuditQuery, page: number) =>
    useQuery({
        queryKey: ["audit", "list", query, page],
        queryFn: () => {
            const params = auditParams(query);
            params.set("page", String(page));
            params.set("pageSize", String(PAGE_SIZE));
            return api<Paged<AuditEntry>>(`/api/audit?${params}`);
        },
        placeholderData: (previous) => previous,
    });

export const useAuditFilters = () =>
    useQuery({
        queryKey: ["audit", "filters"],
        queryFn: () => api<AuditFilters>("/api/audit/filters"),
        staleTime: 5 * 60_000,
    });

// Downloads the matching entries as CSV. The API refuses more than 10,000, with a message saying so.
export const useAuditExport = () =>
    useMutation({
        mutationFn: async (query: AuditQuery) => {
            const blob = await apiBlob(
                `/api/audit/export?${auditParams(query)}`,
            );
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `audit-log.csv`;
            link.click();
            URL.revokeObjectURL(url);
        },
    });
