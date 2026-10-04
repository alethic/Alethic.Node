# Sample.React

A React 19 application served from ASP.NET Core by Alethic.Node.AspNetCore: every page rendered on an embedded Node
engine, with its suspended data resolved into the markup, and hydrated in the browser.

```shell
dotnet run --project samples/Sample.React/Server
```

Then open the address it prints and follow the links: a home page, an about page and a park page, each a server
render.

## What it shows

- **The pool** registered once, with two engines, and the application's module loaded on both at startup.
- **`FetchRequestHandler`** calling the application's `fetch` handler, with nothing written for it: the application
  is a `fetch(request)` that renders a `Response`.
- **A route provider**, `SampleRouteProvider`, reading the application's own router, which the module exports as
  `router`, and translating its routes into `RenderRoute`s, so the site gets an endpoint per route and a 404 the
  application decides on is a real 404.
- **`MapNode`** mapping those routes, with `ConfigureEndpoint` seeing each as it is mounted.

## Layout

- `Server` is the ASP.NET Core project. It references the client project, so building it builds the client and copies
  its two bundles: `ssr/app.cjs`, the `fetch` handler, beside the application, and `wwwroot/app.js`, the browser
  bundle the rendered pages load.
- The client is [`samples/Sample.Client`](../Sample.Client), shared with the Web Forms sample; its `src/app` is this
  application.
