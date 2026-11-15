export function Motif({ className }: { className?: string }) {
    const lanes = [
        { y: 70, planned: 110, actual: 122, tone: "ok" },
        { y: 115, planned: 170, actual: 232, tone: "late" },
        { y: 160, planned: 210, actual: null, tone: "overdue" },
        { y: 205, planned: 350, actual: null, tone: "upcoming" },
    ] as const;

    return (
        <svg viewBox="0 0 520 250" className={className} aria-hidden="true">
            {[70, 170, 270, 370, 470].map((x) => (
                <line
                    key={x}
                    x1={x}
                    x2={x}
                    y1="30"
                    y2="235"
                    className="stroke-chrome-foreground/10"
                />
            ))}

            {lanes.map((lane) => (
                <g key={lane.y}>
                    <line
                        x1="40"
                        x2="500"
                        y1={lane.y}
                        y2={lane.y}
                        className="stroke-chrome-foreground/15"
                    />
                    {lane.actual !== null && (
                        <line
                            x1={lane.planned}
                            x2={lane.actual}
                            y1={lane.y}
                            y2={lane.y}
                            strokeWidth="3"
                            className={
                                lane.tone === "late"
                                    ? "stroke-late"
                                    : "stroke-ok"
                            }
                        />
                    )}
                    {lane.tone === "overdue" && (
                        <line
                            x1={lane.planned}
                            x2="290"
                            y1={lane.y}
                            y2={lane.y}
                            strokeWidth="3"
                            strokeDasharray="6 5"
                            className="stroke-late"
                        />
                    )}
                    <circle
                        cx={lane.planned}
                        cy={lane.y}
                        r="6"
                        strokeWidth="2"
                        className="fill-chrome stroke-chrome-foreground/55"
                    />
                    {lane.actual !== null && (
                        <circle
                            cx={lane.actual}
                            cy={lane.y}
                            r="6"
                            className={
                                lane.tone === "late" ? "fill-late" : "fill-ok"
                            }
                        />
                    )}
                </g>
            ))}

            <g className="motif-now">
                <line
                    x1="290"
                    x2="290"
                    y1="26"
                    y2="235"
                    strokeWidth="2"
                    className="stroke-signal"
                />
                <rect
                    x="268"
                    y="6"
                    width="44"
                    height="20"
                    className="fill-signal"
                />
                <text
                    x="290"
                    y="20.5"
                    textAnchor="middle"
                    fontSize="12"
                    fontWeight="700"
                    className="fill-ink"
                >
                    Now
                </text>
            </g>
        </svg>
    );
}
