import { useState, type ReactNode } from "react";
import { OpButton as Button } from "@/components/operator/OpButton";
import { inputClass } from "./styles";

// A button that opens a small form asking for one piece of text, then does the thing.
export function ReasonAction({
    label,
    question,
    submitLabel,
    tone,
    optional,
    pending,
    onSubmit,
    extra,
    className,
}: {
    label: string;
    question: string;
    submitLabel: string;
    tone?: "danger";
    optional?: boolean;
    pending: boolean;
    onSubmit: (text: string) => void;
    extra?: ReactNode;
    className?: string;
}) {
    const [open, setOpen] = useState(false);
    const [text, setText] = useState("");

    if (!open) {
        return (
            <Button
                variant="outline"
                tone={tone}
                className={className ?? "h-10 px-4"}
                disabled={pending}
                onClick={() => setOpen(true)}
            >
                {label}
            </Button>
        );
    }

    const ok = optional || text.trim().length >= 3;
    return (
        <div className="w-full rounded-sm border bg-muted/40 p-3">
            <label className="block text-sm">
                <span className="mb-1 block font-medium">{question}</span>
                <input
                    autoFocus
                    value={text}
                    onChange={(e) => setText(e.target.value)}
                    maxLength={500}
                    className={inputClass}
                />
            </label>
            {extra}
            <div className="mt-3 flex flex-wrap gap-2">
                <Button
                    tone={tone}
                    className="h-10 px-4"
                    disabled={!ok || pending}
                    onClick={() => {
                        onSubmit(text.trim());
                        setOpen(false);
                        setText("");
                    }}
                >
                    {submitLabel}
                </Button>
                <Button
                    variant="outline"
                    className="h-10 px-4"
                    onClick={() => setOpen(false)}
                >
                    Never mind
                </Button>
            </div>
        </div>
    );
}
