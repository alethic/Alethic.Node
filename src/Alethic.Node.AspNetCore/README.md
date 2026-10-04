# Alethic.Node.AspNetCore

Serve a JavaScript application's pages from ASP.NET Core, rendered on Node.js embedded in the process: a pool of real
Node runtimes ([Alethic.Node](https://www.nuget.org/packages/Alethic.Node)), a request handler that calls the
application's `fetch` handler, and endpoint mapping, from a single fallback to an endpoint per route read from the
application's own router.

Earlier versions were published as `Alethic.AspNetCore.Node`, through 0.3. The types moved to the `Alethic.Node` and
`Alethic.Node.AspNetCore` namespaces.

## Install

```shell
dotnet add package Alethic.Node.AspNetCore
dotnet add package Microsoft.JavaScript.LibNode.linux-x64    # and/or win-x64, linux-arm64, osx-x64, osx-arm64
```

The `LibNode` package carries the native runtime for one platform. Reference the platforms you deploy to.

## Use

```csharp
builder.Services.AddNodeEnginePool(o =>
{
    o.EngineCount = 2;                 // engines are CPU parallelism: match the cores the process may use
    o.MaxConcurrencyPerEngine = 4;     // renders an engine runs at once
});

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();                      // explicit, or the fallback endpoint outruns static files

app.MapNodeFetchHandler(o =>
{
    o.Module = NodeModuleSource.FromFile("ssr/app.cjs");
    o.Environment["ApiBaseUri"] = "http://api.internal:8080/";
});

app.Run();
```

That mounts the application on one fallback endpoint, `/{**path}`, behind everything else the site maps. The module
is loaded on every engine at startup, so a module that does not load fails the start, not the first request.

## The application

`ssr/app.cjs` is a self-contained CommonJS bundle whose default export has a `fetch(request, env, ctx)` function
returning a `Response`, or is that function. The protocol is
[Alethic.Node.Http](https://www.nuget.org/packages/Alethic.Node.Http)'s, shared with the System.Web host; its README
says exactly what the application sees. In brief: the request's URL is relative to `BaseUri`, the visitor's address
is in the `X-Forwarded-*` headers, `env` carries the strings in `Environment`, and the response's status, headers and
body become the site's.

The request body reaches the application as a stream, so an upload is never held in memory on its way through; set
`RequestBody` to `Buffered` where the application reads it more than once. The response body streams by default; set
`ResponseBody` to `Buffered` where a failure partway through should fail the request rather than truncate it.

## An endpoint per route

A `fetch` handler says nothing about routes. For an endpoint per route, pair the handler with an
`INodeRouteProvider`, written against your framework to read the router the application already dispatches on:

```csharp
sealed class MyRouteProvider(NodeEnginePool pool, NodeModuleSource module) : INodeRouteProvider
{
    public Task<IReadOnlyList<RenderRoute>> GetRoutesAsync(CancellationToken cancellationToken = default) =>
        pool.RunAsync(module, exports =>
        {
            // On the engine's thread: read the routes out, and return plain .NET data.
            var routes = NodeModuleExports.Default(exports)["routes"];
            ...
        }, cancellationToken);
}
```

```csharp
var pool = app.Services.GetRequiredService<NodeEnginePool>();
var module = NodeModuleSource.FromFile("ssr/app.cjs");

app.MapNode(
    new FetchRequestHandler(pool, new FetchRequestHandlerOptions() { Module = module }),
    new MyRouteProvider(pool, module));
```

Naming the same module on the same pool is all the sharing the two need: Node loads it once per engine, and both see
that instance.

Each `RenderRoute` has a `Pattern`, a URLPattern pathname such as `/parks/:parkRef`, which is converted to an ASP.NET
route template; an `Id`, which names the endpoint, so `LinkGenerator.GetPathByName` builds URLs from the router; and a
`RenderMode`:

| `RenderMode` | |
| --- | --- |
| `Server` | Rendered per request. |
| `Prerender` | Served like `Server`; the distinction is for caching policy. |
| `Client` | Not mapped: whatever serves the application shell keeps serving it. |

`MapNode` maps an endpoint per route, plus the fallback for whatever no route claims (`MapNodeOptions.FallbackPattern`;
null for none), calls `MapNodeOptions.ConfigureEndpoint` for each so you can attach caching or authorization by route,
and returns an `IEndpointConventionBuilder` over all of them. Each endpoint carries its `RenderRoute` as metadata. A
pattern a route template cannot express falls to the fallback. A provider that throws fails the start: a provider
that cannot read its router is broken, which must not pass for an application with no routes.

## Another protocol

`INodeRequestHandler` is two methods, `PrepareAsync` and `HandleAsync(HttpContext)`. `FetchRequestHandler` is the stock
implementation; an application whose server protocol is not a `fetch` handler gets another, mounted the same way and
running on the same engines. A host with something to add to every request derives from `FetchRequestHandler` and
overrides `HandleAsync`.

## The pool on its own

The pool is `Alethic.Node`'s and runs any JavaScript, web or not: `pool.RunAsync(module, exports => ...)` puts you on
an engine's thread with the module's exports. See that package's README for leases, the adaptive mode, statistics and
metrics.

## Constraints

- **Engine count must be configured.** Inside a container the processor count reports the host's cores, not the
  quota. One engine already overlaps many renders, since everything the application awaits yields to its event loop;
  engines are for CPU parallelism.
- **CommonJS only, bundled and unsplit.** The embedded runtime cannot `import()`.
- **Module scope is shared by every render on an engine.** Per-request state belongs in the request.
- **A rebuilt bundle is not picked up** until the server restarts.
- **A streamed response cannot change its status** once its first byte is out. Buffer the response where that
  matters.
- **Cancelling the request aborts the render**, through the `AbortSignal` on the `Request`.
