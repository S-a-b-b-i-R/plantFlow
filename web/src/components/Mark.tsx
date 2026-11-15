// The logo: a planned ring, a line, and the dot where it actually happened.
export function Mark({ className }: { className?: string }) {
    return (
        <svg viewBox="0 0 32 32" className={className} aria-hidden="true">
            <rect width="32" height="32" rx="6" fill="#f4c20d" />
            <circle
                cx="9"
                cy="16"
                r="3.2"
                fill="none"
                stroke="#1b2a31"
                strokeWidth="2"
            />
            <line
                x1="12.2"
                y1="16"
                x2="20"
                y2="16"
                stroke="#1b2a31"
                strokeWidth="2"
            />
            <circle cx="23" cy="16" r="3.2" fill="#1b2a31" />
        </svg>
    );
}
