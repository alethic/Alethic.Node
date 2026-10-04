# Sample.Client

The React client both web samples share, built by Vite into their bundles. A Yarn 4 project (`corepack enable`
once), built by the samples' server projects as they build, or by hand:

```shell
yarn install
yarn run build
```

| Bundle | For |
| --- | --- |
| `dist/aspnet/app.js`, `dist/ssr/app.cjs` | Sample.React, and Sample.WebForms' full pages: the application, its `fetch` handler and router. |
| `dist/systemweb/client.js`, `dist/systemweb-ssr/server.cjs` | Sample.WebForms' components: `outlet` and `render`, with the components. |

`src/app` is the application, `src/components` the components, `src/systemweb` the Web Forms entries and
`outlets.tsx`, which gives the page one React tree with each component rendered into its element.
