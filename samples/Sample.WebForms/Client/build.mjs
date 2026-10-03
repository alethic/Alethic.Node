// Builds the client to the paths NodeComponent looks in by default, beside the site:
//
//   ../Server/components/client.js            the browser entry, an ES module
//   ../Server/App_Data/components/server.cjs  the server bundle, one CommonJS file with everything in it
//
// The server bundle runs on Node embedded in the worker process, which resolves nothing but Node's
// built-ins: every dependency goes inside it, without code splitting.

import { build } from "esbuild";

const common = {
    bundle: true,
    jsx: "automatic",
    define: { "process.env.NODE_ENV": JSON.stringify("production") },
    logLevel: "warning",
};

await build({
    ...common,
    entryPoints: ["src/client.tsx"],
    outfile: "../Server/components/client.js",
    format: "esm",
    platform: "browser",
    target: "es2022",
    minify: true,
});

await build({
    ...common,
    entryPoints: ["src/server.tsx"],
    outfile: "../Server/App_Data/components/server.cjs",
    format: "cjs",
    platform: "node",
    target: "node20",
});
