export const inputClass =
    "h-10 w-full rounded-sm border bg-card px-3 text-sm placeholder:text-muted-foreground";

export const textareaClass =
    "w-full rounded-sm border bg-card px-3 py-2 text-sm placeholder:text-muted-foreground";

export const panelClass = "rounded-sm border bg-card";

export const nextButtonClass =
    "bg-signal font-semibold text-ink hover:bg-signal/90";

// A tab on a page with several lists. The open tab is filled with signal yellow.
export const tabClass = (active: boolean) =>
    [
        "inline-flex h-11 shrink-0 items-center gap-2 rounded-sm border px-4 text-sm font-semibold whitespace-nowrap",
        active
            ? "border-signal bg-signal text-ink"
            : "bg-card hover:border-ink",
    ].join(" ");

// The row that holds the tabs. On a phone it scrolls sideways instead of wrapping; the cut-off
// last tab shows there is more, so the scrollbar is hidden.
export const tabRowClass =
    "-mx-4 flex gap-2 overflow-x-auto px-4 [scrollbar-width:none] sm:mx-0 sm:px-0";
