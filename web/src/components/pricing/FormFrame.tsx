import type { ReactNode } from "react";
import type { UseMutationResult } from "@tanstack/react-query";
import { FormShell } from "./FormShell";

// Connects a form's fields to its save mutation: Save sends the body, then closes the form.
export function FormFrame({
    title,
    canSave,
    save,
    body,
    onDone,
    onCancel,
    children,
    saveLabel,
}: {
    title: string;
    canSave: boolean;
    save: UseMutationResult<unknown, Error, unknown>;
    body: unknown;
    onDone: () => void;
    onCancel: () => void;
    children: ReactNode;
    saveLabel?: string;
}) {
    return (
        <FormShell
            title={title}
            canSave={canSave}
            saving={save.isPending}
            error={save.isError ? save.error.message : null}
            onSave={() => save.mutate(body, { onSuccess: onDone })}
            onCancel={onCancel}
            saveLabel={saveLabel}
        >
            {children}
        </FormShell>
    );
}
