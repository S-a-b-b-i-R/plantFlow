import { Navigate, Route, Routes } from "react-router-dom";
import { Gate } from "@/auth/Gate";
import { AppShell } from "@/layout/AppShell";
import { LiveBoard } from "@/pages/LiveBoard";

export default function App() {
    return (
        <Routes>
            <Route element={<Gate />}>
                <Route element={<AppShell />}>
                    <Route index element={<Navigate to="/live" replace />} />
                    <Route path="live" element={<LiveBoard />} />
                    <Route
                        path="*"
                        element={<p className="text-sm">Page not found.</p>}
                    />
                </Route>
            </Route>
        </Routes>
    );
}
