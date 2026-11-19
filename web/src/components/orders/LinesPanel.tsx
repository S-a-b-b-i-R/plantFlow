import { LuTriangleAlert } from "react-icons/lu";
import { panelClass } from "./styles";
import { cft } from "@/lib/format";
import {
    elementLabel,
    methodLabel,
    money,
    type OrderCharge,
    type OrderLine,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

export function LinesPanel({
    lines,
    charges,
}: {
    lines: OrderLine[];
    charges: OrderCharge[];
}) {
    const total = lines.reduce((sum, l) => sum + l.lineValue, 0);
    const quantity = lines.reduce((sum, l) => sum + l.quantityCft, 0);

    return (
        <section
            className={cn(panelClass, "overflow-x-auto")}
            aria-label="Order lines"
        >
            <header className="border-b px-4 py-3">
                <h2 className="display text-2xl">What is ordered</h2>
            </header>
            <table className="w-full min-w-168 text-sm">
                <thead className="border-b bg-muted/50 text-left text-xs font-medium text-muted-foreground">
                    <tr>
                        <th className="px-4 py-2">Mix</th>
                        <th className="px-4 py-2">Pour</th>
                        <th className="px-4 py-2 text-right">CFT</th>
                        <th className="px-4 py-2 text-right">Price</th>
                        <th className="px-4 py-2 text-right">Floor</th>
                        <th className="px-4 py-2 text-right">Value</th>
                    </tr>
                </thead>
                <tbody className="divide-y">
                    {lines.map((l) => (
                        <tr
                            key={l.id}
                            className={l.belowFloor ? "bg-late/5" : undefined}
                        >
                            <td className="px-4 py-2">
                                <span className="font-medium">
                                    {l.productCode}
                                </span>
                                <span className="block text-xs text-muted-foreground">
                                    {l.productName}
                                </span>
                            </td>
                            <td className="px-4 py-2 text-muted-foreground">
                                {elementLabel[l.pourElement]},{" "}
                                {methodLabel[l.pourMethod]}
                                <span className="block text-xs">
                                    Slump {l.slumpMm} mm
                                </span>
                                {l.notes && (
                                    <span className="block text-xs">
                                        {l.notes}
                                    </span>
                                )}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {cft(l.quantityCft)}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {money(l.unitPrice)}
                                {l.listPrice !== null &&
                                    l.listPrice !== l.unitPrice && (
                                        <span className="block text-xs text-muted-foreground">
                                            List {money(l.listPrice)}
                                        </span>
                                    )}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {l.floorPrice !== null
                                    ? money(l.floorPrice)
                                    : "Not checked"}
                                {l.belowFloor && (
                                    <span className="mt-0.5 flex items-center justify-end gap-1 text-xs font-medium text-late">
                                        <LuTriangleAlert
                                            aria-hidden
                                            className="size-3.5"
                                        />
                                        Below floor
                                    </span>
                                )}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {money(l.lineValue)}
                            </td>
                        </tr>
                    ))}
                </tbody>
                <tfoot className="border-t font-semibold">
                    {charges.map((c, i) => (
                        <tr key={`${c.kind}-${i}`} className="font-normal">
                            <td className="px-4 py-2" colSpan={2}>
                                {c.description}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {cft(c.quantityCft)}
                            </td>
                            <td className="px-4 py-2 text-right tabular-nums">
                                {money(c.ratePerCft)}
                            </td>
                            <td />
                            <td className="px-4 py-2 text-right tabular-nums">
                                {money(c.amount)}
                            </td>
                        </tr>
                    ))}
                    <tr>
                        <td className="px-4 py-2" colSpan={2}>
                            Concrete total
                        </td>
                        <td className="px-4 py-2 text-right tabular-nums">
                            {cft(quantity)}
                        </td>
                        <td colSpan={2} />
                        <td className="px-4 py-2 text-right tabular-nums">
                            {money(total)}
                        </td>
                    </tr>
                </tfoot>
            </table>
        </section>
    );
}
