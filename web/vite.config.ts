import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
    plugins: [react(), tailwindcss()],
    resolve: {
        alias: { "@": path.resolve(import.meta.dirname, "./src") },
    },
    server: {
        // The API runs on 5026. Proxying means the browser sees one origin, so no CORS setup is needed in development.
        proxy: { "/api": "http://localhost:5026" },
    },
});
