import react from "@vitejs/plugin-react";
import { defineConfig, UserConfig } from "vite";

/**
 * Four bundles from one source tree, one per VITE_ENTRY, each for one of the samples:
 *
 * - `aspnet`: the ASP.NET Core sample's browser entry, `app.js`, which hydrates the document.
 * - `ssr`: the ASP.NET Core sample's server entry, `app.cjs`, a `fetch` handler and its router.
 * - `systemweb`: the Web Forms sample's browser entry, `client.js`, exporting `outlet` and the components.
 * - `systemweb-ssr`: the Web Forms sample's server bundle, `server.cjs`, exporting `render`.
 *
 * The server bundles run on Node embedded in a .NET process, whose `require` reads built-ins only, so
 * everything else goes inside them, in one file: the embedded runtime registers no dynamic-import
 * callback, so a split chunk would never load.
 */
export default defineConfig(({ mode }) => {
    const entry = process.env.VITE_ENTRY;
    const server = entry === "ssr" || entry === "systemweb-ssr";

    const config: UserConfig = {
        plugins: [react()],
        publicDir: false,
        ssr: {
            noExternal: true,
        },
        // React picks its development or production build by this, which Node does not set.
        define: server ? { "process.env.NODE_ENV": JSON.stringify(mode) } : undefined,
        build: {
            manifest: false,
            minify: mode === "production",
            sourcemap: mode !== "production",
        },
    };

    switch (entry) {
        case "aspnet":
            config.build!.rollupOptions = {
                input: "src/app/entry-aspnet.tsx",
                output: { codeSplitting: false, entryFileNames: "app.js" },
            };
            break;

        case "ssr":
            config.build!.ssr = "src/app/entry-ssr.tsx";
            config.build!.target = "node20";
            config.build!.rollupOptions = {
                output: { format: "cjs", codeSplitting: false, entryFileNames: "app.cjs" },
            };
            break;

        case "systemweb":
            config.build!.rollupOptions = {
                input: "src/systemweb/entry-systemweb.tsx",
                // The entry is used by what it exports, which an application build would drop.
                preserveEntrySignatures: "strict",
                output: { codeSplitting: false, entryFileNames: "client.js" },
            };
            break;

        case "systemweb-ssr":
            config.build!.ssr = "src/systemweb/entry-systemweb-ssr.tsx";
            config.build!.target = "node20";
            config.build!.rollupOptions = {
                output: { format: "cjs", codeSplitting: false, entryFileNames: "server.cjs" },
            };
            break;

        default:
            throw new Error(`VITE_ENTRY must be aspnet, ssr, systemweb or systemweb-ssr, not ${entry}.`);
    }

    return config;
});
