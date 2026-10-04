# Sample.Client

The React client both web samples share: one source tree, built by Vite into each sample's bundles.

It is a Yarn 4 project; Corepack provides Yarn: `corepack enable` once per machine. The `.esproj` builds it on
`dotnet build`, so the samples' server projects, which reference it, build it as they build. By hand:

```shell
yarn install
yarn run build        # every bundle; build:release for production builds
yarn run typecheck
```

## The bundles

| Bundle | Entry | For |
| --- | --- | --- |
| `dist/aspnet/app.js` | `src/app/entry-aspnet.tsx` | Sample.React's browser: hydrates the rendered page. |
| `dist/ssr/app.cjs` | `src/app/entry-ssr.tsx` | Sample.React's server, and Sample.WebForms' full pages: a `fetch` handler rendering whole documents, and the router. |
| `dist/systemweb/client.js` | `src/systemweb/entry-systemweb.tsx` | Sample.WebForms' browser: `outlet`, and the components a page may place. |
| `dist/systemweb-ssr/server.cjs` | `src/systemweb/entry-systemweb-ssr.tsx` | Sample.WebForms' server: `render`, and the same components. |

The server bundles are CommonJS with everything inside them and no code splitting, since the embedded runtime's
`require` reads built-ins only and cannot `import()`.

## Layout

- `src/app`: the full-page application: its router, `App`, and the two entries.
- `src/components`: the components the Web Forms pages host, and `index.ts`, which lists them, including the
  `Catalog` object a dotted `Name` reaches into.
- `src/systemweb`: the Web Forms entries, and `outlets.tsx`, which makes `outlet`: one React tree for the page, each
  component rendered into its element through a portal, so all of them share the providers, with `update` keeping a
  component's state across a partial postback.
- `src/providers.tsx`: what every component sits under: a cache context, and the cart the demo page shares between
  components.
