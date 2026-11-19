import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";
import { OpButton as Button } from "@/components/operator/OpButton";
import { ConcreteStep } from "./ConcreteStep";
import { CustomerStep } from "./CustomerStep";
import { ReviewStep } from "./ReviewStep";
import {
    blankLine,
    draftFrom,
    emptyDraft,
    parseAmount,
    toRequest,
    validPrice,
    validQuantity,
    type Draft,
    type LineDraft,
} from "./orderDraft";
import { nextButtonClass } from "./styles";
import { useStoredDistance } from "./useStoredDistance";
import { api } from "@/lib/api";
import type {
    OrderDetail,
    Paged,
    PlantOption,
    ProductOption,
    SiteOption,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

export type WizardMode = "create" | "edit" | "revise" | "amend";

const steps = ["Customer and site", "Concrete", "Review"] as const;

const copy: Record<
    WizardMode,
    { title: string; explain: string; save: string }
> = {
    create: {
        title: "New order",
        explain:
            "Saving makes a draft. You submit it for approval from the order page.",
        save: "Save draft",
    },
    edit: {
        title: "Edit draft",
        explain: "Only a draft can be edited. Saving keeps it a draft.",
        save: "Save changes",
    },
    revise: {
        title: "Revise order",
        explain:
            "A changed price, quantity, line, distance or date restarts the Management and Credit approvals.",
        save: "Save revision",
    },
    amend: {
        title: "Amend confirmed order",
        explain:
            "A higher total, or a below-floor price with a commercial change, sends the order back to Management and Credit before it is confirmed again.",
        save: "Save amendment",
    },
};

export function OrderWizard({
    mode,
    existing,
}: {
    mode: WizardMode;
    existing?: OrderDetail;
}) {
    const navigate = useNavigate();
    const queryClient = useQueryClient();
    const [step, setStep] = useState(0);
    const [draft, setDraft] = useState<Draft>(() =>
        existing ? draftFrom(existing) : emptyDraft(),
    );
    const [reason, setReason] = useState("");
    const counter = useRef(1);

    const products = useQuery({
        queryKey: ["products", "active"],
        queryFn: () =>
            api<Paged<ProductOption>>(
                "/api/products?status=Active&pageSize=100",
            ),
    });
    const sites = useQuery({
        queryKey: ["sites", draft.customerId],
        queryFn: () =>
            api<Paged<SiteOption>>(
                `/api/customers/${draft.customerId}/sites?pageSize=100`,
            ),
        enabled: !!draft.customerId,
    });
    const plants = useQuery({
        queryKey: ["plants", "active"],
        queryFn: () =>
            api<Paged<PlantOption>>("/api/plants?status=Active&pageSize=100"),
    });
    const stored = useStoredDistance(draft.siteId, draft.plantId);

    // Lines on an existing order can keep an inactive mix, so include what the order already has.
    const productList = [
        ...(products.data?.items ?? []),
        ...(existing?.lines ?? [])
            .filter(
                (l) =>
                    !(products.data?.items ?? []).some(
                        (p) => p.id === l.productId,
                    ),
            )
            .map((l): ProductOption => ({
                id: l.productId,
                code: l.productCode,
                name: l.productName,
                strengthClass: "",
                defaultSlumpMm: l.slumpMm,
                status: "Inactive",
            })),
    ];

    const change = (patch: Partial<Draft>) =>
        setDraft((d) => ({ ...d, ...patch }));
    const updateLine = (key: string, patch: Partial<LineDraft>) =>
        setDraft((d) => ({
            ...d,
            lines: d.lines.map((l) => (l.key === key ? { ...l, ...patch } : l)),
        }));
    const addLine = () =>
        setDraft((d) => ({
            ...d,
            lines: [...d.lines, blankLine(`n${counter.current++}`)],
        }));
    const removeLine = (key: string) =>
        setDraft((d) => ({
            ...d,
            lines: d.lines.filter((l) => l.key !== key),
        }));

    const distanceOk =
        draft.distance.trim() !== ""
            ? parseAmount(draft.distance) !== null &&
              Number(draft.distance) >= 0
            : stored.km !== null;
    const firstOk =
        !!draft.customerId &&
        !!draft.siteId &&
        !!draft.plantId &&
        !!draft.date &&
        distanceOk;
    const secondOk =
        draft.lines.length > 0 &&
        draft.lines.every(
            (l) =>
                !!l.productId &&
                validQuantity(l.quantity) &&
                validPrice(l.unitPrice),
        );
    const needsReason = mode === "revise" || mode === "amend";
    const reasonOk = !needsReason || reason.trim().length >= 3;

    const save = useMutation({
        mutationFn: async () => {
            const body = toRequest(draft);
            if (mode === "create") {
                const created = await api<OrderDetail>("/api/orders", {
                    method: "POST",
                    body: JSON.stringify(body),
                });
                return created.order.id;
            }
            const id = existing!.order.id;
            if (mode === "edit") {
                await api<void>(`/api/orders/${id}`, {
                    method: "PUT",
                    body: JSON.stringify(body),
                });
            } else {
                await api<void>(`/api/orders/${id}/${mode}`, {
                    method: "PUT",
                    body: JSON.stringify({
                        reason: reason.trim(),
                        order: body,
                    }),
                });
            }
            return id;
        },
        onSuccess: async (id) => {
            await Promise.all([
                queryClient.invalidateQueries({ queryKey: ["order"] }),
                queryClient.invalidateQueries({ queryKey: ["orders"] }),
                queryClient.invalidateQueries({ queryKey: ["invoice"] }),
            ]);
            navigate(`/orders/${id}`);
        },
    });

    const siteName =
        sites.data?.items.find((s) => s.id === draft.siteId)?.name ??
        existing?.order.siteName ??
        "";
    const plant = plants.data?.items.find((p) => p.id === draft.plantId);
    const ready = [
        firstOk,
        firstOk && secondOk,
        firstOk && secondOk && reasonOk,
    ];

    return (
        <div className="mx-auto max-w-3xl space-y-6">
            <header>
                <Link
                    to={existing ? `/orders/${existing.order.id}` : "/orders"}
                    className="text-sm text-muted-foreground hover:text-foreground"
                >
                    {existing
                        ? `Back to ${existing.order.number}`
                        : "Back to orders"}
                </Link>
                <h1 className="display mt-1 text-4xl leading-none">
                    {copy[mode].title}
                    {existing ? ` ${existing.order.number}` : ""}
                </h1>
            </header>

            <ol className="grid grid-cols-3 gap-2" aria-label="Steps">
                {steps.map((label, i) => {
                    const reachable =
                        i === 0 ||
                        (i === 1 && firstOk) ||
                        (i === 2 && firstOk && secondOk);
                    return (
                        <li key={label}>
                            <button
                                type="button"
                                disabled={!reachable}
                                aria-current={i === step ? "step" : undefined}
                                onClick={() => setStep(i)}
                                className={cn(
                                    "flex h-12 w-full items-center gap-3 rounded-sm border px-3 text-left text-sm disabled:cursor-not-allowed disabled:opacity-50",
                                    i === step
                                        ? "border-signal bg-signal font-semibold text-ink"
                                        : "bg-card hover:border-ink",
                                )}
                            >
                                <span className="display text-xl leading-none">
                                    {i + 1}
                                </span>
                                <span className="truncate">{label}</span>
                            </button>
                        </li>
                    );
                })}
            </ol>

            {step === 0 && (
                <CustomerStep
                    draft={draft}
                    change={change}
                    locked={mode === "revise" || mode === "amend"}
                />
            )}
            {step === 1 && (
                <ConcreteStep
                    draft={draft}
                    products={productList}
                    updateLine={updateLine}
                    addLine={addLine}
                    removeLine={removeLine}
                />
            )}
            {step === 2 && (
                <ReviewStep
                    draft={draft}
                    products={productList}
                    siteName={siteName}
                    plant={plant}
                    reason={reason}
                    setReason={setReason}
                    needsReason={needsReason}
                    explain={copy[mode].explain}
                />
            )}

            {save.isError && (
                <p className="text-sm font-medium text-late" role="alert">
                    {save.error.message}
                </p>
            )}

            <footer className="flex flex-wrap items-center justify-between gap-3 border-t pt-4">
                <Button
                    variant="outline"
                    className="h-11 px-5"
                    disabled={step === 0 || save.isPending}
                    onClick={() => setStep(step - 1)}
                >
                    Back
                </Button>
                {step < 2 ? (
                    <Button
                        className={cn(
                            "h-11 px-6",
                            ready[step] && nextButtonClass,
                        )}
                        disabled={!ready[step]}
                        onClick={() => setStep(step + 1)}
                    >
                        Next
                    </Button>
                ) : (
                    <Button
                        className={cn("h-11 px-6", ready[2] && nextButtonClass)}
                        disabled={!ready[2] || save.isPending}
                        onClick={() => save.mutate()}
                    >
                        {save.isPending ? "Saving…" : copy[mode].save}
                    </Button>
                )}
            </footer>
        </div>
    );
}
