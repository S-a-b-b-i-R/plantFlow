import type { ReactNode } from "react";

export function Field({
    label,
    hint,
    children,
    className,
}: {
    label: string;
    hint?: string;
    children: ReactNode;
    className?: string;
}) {
    return (
        <label className={`block text-sm ${className ?? ""}`}>
            <span className="mb-1 block font-medium">{label}</span>
            {children}
            {hint && (
                <span className="mt-1 block text-xs text-muted-foreground">
                    {hint}
                </span>
            )}
        </label>
    );
}
