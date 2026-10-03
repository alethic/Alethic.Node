# @alethic/node-aspnet-react

The JavaScript half of [Alethic.Node.AspNet.React](../Alethic.Node.AspNet.React): React components hosted on ASP.NET
Web Forms pages by its `ReactComponent` control. A client builds two bundles from it.

## The browser entry

```tsx
import { createOutlets } from "@alethic/node-aspnet-react/client";

export const outlet = createOutlets({
    providers: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
});

export { GreetingPanel } from "./GreetingPanel";
export { GreetingBadge } from "./GreetingBadge";
```

Build this as an ES module that keeps its exports, for example as a library entry. The control imports it with
`import()` and calls `outlet(m[Component], element, props)`.

The first component placed creates one hidden React root for the page, wrapped in `providers`. Each component renders
into its own element through a portal, inside its own error boundary and suspense boundary. When an element leaves the
page, for example in an `UpdatePanel`'s partial postback, its component is removed with it.

## The server bundle

```tsx
import { createRenderOutlets } from "@alethic/node-aspnet-react/server";
import * as components from "./components";

export const renderOutlets = createRenderOutlets(components, {
    // Once per page, so nothing is shared between visitors.
    providers: () => {
        const client = new QueryClient();
        return ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
    },
});
```

The bundle runs on libnode embedded in the worker process, and that runtime resolves nothing but Node's built-ins. Build
it as one CommonJS file, with every dependency inside it:
- no code splitting, because the embedded runtime does not resolve `import()`;
- `process.env.NODE_ENV` defined, so that React picks a build.

With esbuild:

```sh
esbuild server.tsx --bundle --platform=node --format=cjs --target=node20 --define:process.env.NODE_ENV='"production"'
```

The page's components render one after another, each with `prerender` from `react-dom/static`, which waits for the data
each one suspends on. Each callback in the props raises its command on the page and returns a promise of the result.

A `fetch` of the site is answered by the site itself, in process, as the visitor. That covers a relative URL, or an
absolute one on the page's own origin.

A component fails if it throws, or if it leaves a callback's rejection unhandled. Its control then throws on the page.
