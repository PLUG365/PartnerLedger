import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { powerApps } from "@microsoft/power-apps-vite";

export default defineConfig({
  plugins: [react(), powerApps()],
  base: "./",
  server: { host: "127.0.0.1", port: 5173, strictPort: true },
});
