import type { ComponentProps } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type Props = ComponentProps<typeof Button> & { tone?: "danger" };

// The shared Button, made clearer for the console: a disabled button still looks like a button,
// and the ordinary and outline ones react visibly when pointed at.
export function OpButton({ variant, tone, className, ...props }: Props) {
    const outline = variant === "outline";
    const ghost = variant === "ghost";

    const look =
        tone === "danger"
            ? outline
                ? "border-late/40 bg-card text-late hover:border-late hover:bg-late hover:text-white"
                : "bg-late text-white hover:bg-late"
            : outline
              ? "border-ink/30 bg-card text-foreground hover:border-ink hover:bg-ink hover:text-white"
              : ghost
                ? "hover:bg-ink/10"
                : "bg-ink text-white hover:bg-ink";

    return (
        <Button
            variant={variant}
            className={cn(
                look,
                "disabled:pointer-events-auto disabled:cursor-not-allowed disabled:border disabled:border-dashed disabled:border-border disabled:bg-muted disabled:text-muted-foreground disabled:opacity-100 disabled:shadow-none",
                className,
            )}
            {...props}
        />
    );
}
