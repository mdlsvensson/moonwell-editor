import { svelte } from "@sveltejs/vite-plugin-svelte";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// "./" keeps the built page's links relative, so it loads from the app's virtual host folder.
export default defineConfig({
  base: "./",
  plugins: [svelte(), tailwindcss()],
  server: { port: 5173, strictPort: true },
});
