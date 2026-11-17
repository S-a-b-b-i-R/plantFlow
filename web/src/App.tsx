import { Route, Routes } from "react-router-dom";
import { Gate } from "@/auth/Gate";
import { RequirePermission } from "@/auth/RequirePermission";
import { AppShell } from "@/layout/AppShell";
import { Home } from "@/pages/Home";
import { LiveBoard } from "@/pages/LiveBoard";
import { Operator } from "@/pages/Operator";
import { Quality } from "@/pages/Quality";

export default function App() {
    return (
        <Routes>
            <Route element={<Gate />}>
                <Route element={<AppShell />}>
                    <Route index element={<Home />} />
                    <Route element={<RequirePermission />}>
                        <Route path="live" element={<LiveBoard />} />
                        <Route path="operator" element={<Operator />} />
                        <Route path="quality" element={<Quality />} />
                    </Route>
                    <Route
                        path="*"
                        element={<p className="text-sm">Page not found.</p>}
                    />
                </Route>
            </Route>
        </Routes>
    );
}
