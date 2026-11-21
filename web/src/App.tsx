import { Route, Routes } from "react-router-dom";
import { Gate } from "@/auth/Gate";
import { RequirePermission } from "@/auth/RequirePermission";
import { AppShell } from "@/layout/AppShell";
import { CustomerPage } from "@/pages/CustomerPage";
import { Customers } from "@/pages/Customers";
import { CustomerLedger } from "@/pages/CustomerLedger";
import { Ledger } from "@/pages/Ledger";
import { Home } from "@/pages/Home";
import { LiveBoard } from "@/pages/LiveBoard";
import { Operator } from "@/pages/Operator";
import { OrderForm } from "@/pages/OrderForm";
import { OrderPage } from "@/pages/OrderPage";
import { Orders } from "@/pages/Orders";
import { Planning } from "@/pages/Planning";
import { SiteSurveys } from "@/pages/SiteSurveys";
import { SurveyForm } from "@/pages/SurveyForm";
import { Surveys } from "@/pages/Surveys";
import { Pricing } from "@/pages/Pricing";
import { PlantPage } from "@/pages/PlantPage";
import { Catalogue } from "@/pages/Catalogue";
import { Fleet } from "@/pages/Fleet";
import { Plants } from "@/pages/Plants";
import { Quality } from "@/pages/Quality";
import { SalesReps } from "@/pages/SalesReps";
import { Settings } from "@/pages/Settings";
import { AuditLog } from "@/pages/AuditLog";
import { Platform } from "@/pages/Platform";
import { Roles } from "@/pages/Roles";
import { Setup } from "@/pages/Setup";
import { Users } from "@/pages/Users";

export default function App() {
    return (
        <Routes>
            <Route element={<Gate />}>
                <Route element={<AppShell />}>
                    <Route index element={<Home />} />
                    <Route element={<RequirePermission />}>
                        <Route path="live" element={<LiveBoard />} />
                        <Route path="orders" element={<Orders />} />
                        <Route path="orders/new" element={<OrderForm />} />
                        <Route path="orders/:id" element={<OrderPage />} />
                        <Route path="orders/:id/edit" element={<OrderForm />} />
                        <Route path="planning" element={<Planning />} />
                        <Route path="pricing" element={<Pricing />} />
                        <Route path="pricing/:tab" element={<Pricing />} />
                        <Route path="surveys" element={<Surveys />} />
                        <Route path="surveys/:siteId" element={<SiteSurveys />} />
                        <Route
                            path="surveys/requests/:orderId"
                            element={<SurveyForm />}
                        />
                        <Route path="operator" element={<Operator />} />
                        <Route path="quality" element={<Quality />} />
                        <Route path="customers" element={<Customers />} />
                        <Route
                            path="customers/:id"
                            element={<CustomerPage />}
                        />
                        <Route path="sales-reps" element={<SalesReps />} />
                        <Route path="ledger" element={<Ledger />} />
                        <Route
                            path="ledger/:customerId"
                            element={<CustomerLedger />}
                        />
                        <Route path="plants" element={<Plants />} />
                        <Route path="plants/:id" element={<PlantPage />} />
                        <Route path="fleet" element={<Fleet />} />
                        <Route path="catalogue" element={<Catalogue />} />
                        <Route path="users" element={<Users />} />
                        <Route path="roles" element={<Roles />} />
                        <Route path="audit" element={<AuditLog />} />
                        <Route path="setup" element={<Setup />} />
                        <Route path="settings" element={<Settings />} />
                        <Route path="platform" element={<Platform />} />
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
