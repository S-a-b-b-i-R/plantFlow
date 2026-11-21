import type { FormEvent, ReactNode } from "react";
import { OpButton as Button } from "@/components/operator/OpButton";
import { nextButtonClass, panelClass } from "@/components/orders/styles";
import { cn } from "@/lib/utils";

// The frame every price book form shares: a title, the fields, the error, Save and Cancel.
export function FormShell({
    title,
    children,
    canSave,
    saving,
    error,
    onSave,
    onCancel,
    saveLabel = "Save draft",
}: {
    title: string;
    children: ReactNode;
    canSave: boolean;
    saving: boolean;
    error: string | null;
    onSave: () => void;
    onCancel: () => void;
    saveLabel?: string;
}) {
    const submit = (event: FormEvent) => {
        event.preventDefault();
        if (canSave && !saving) onSave();
    };

    return (
        <form onSubmit={submit} className={cn(panelClass, "space-y-4 p-4")}>
            <h2 className="display text-2xl">{title}</h2>
            <div className="grid gap-3 sm:grid-cols-2">{children}</div>
            {error && (
                <p className="text-sm font-medium text-late" role="alert">
                    {error}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    type="submit"
                    className={cn("h-10 px-5", nextButtonClass)}
                    disabled={!canSave || saving}
                >
                    {saving ? "Saving…" : saveLabel}
                </Button>
                <Button
                    type="button"
                    variant="outline"
                    className="h-10 px-4"
                    onClick={onCancel}
                >
                    Cancel
                </Button>
            </div>
        </form>
    );
}
