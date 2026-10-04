# Alethic.Node.AspNetCore

Serve a JavaScript application's pages from ASP.NET Core, rendered on Node.js embedded in the process
([Alethic.Node](https://www.nuget.org/packages/Alethic.Node)). Published as `Alethic.AspNetCore.Node` through 0.3.

```shell
dotnet add package Alethic.Node.AspNetCore
dotnet add package Microsoft.JavaScript.LibNode.linux-x64    # the native runtime: one per platform you deploy to
```

## Use

```csharp
builder.Services.AddNodeEnginePool(o => o.EngineCount = 2);

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

`app.cjs` is a CommonJS bundle whose default export has a `fetch(request, env, ctx)` function returning a `Response`,
or is one; [Alethic.Node.Http](https://www.nuget.org/packages/Alethic.Node.Http) says what it sees. The module loads
on every engine at startup. `RequestBody` and `ResponseBody` are `Streamed` unless set to `Buffered`.

## An endpoint per route

`MapNodeFetchHandler` mounts one fallback endpoint. For an endpoint per route, write an `INodeRouteProvider` that
reads the application's own router, and pass it with the handler:

```csharp
sealed class MyRouteProvider(NodeEnginePool pool, NodeModuleSource module) : INodeRouteProvider
{
    public Task<IReadOnlyList<RenderRoute>> GetRoutesAsync(CancellationToken cancellationToken = default) =>
        pool.RunAsync(module, exports => { /* read the routes out, as RenderRoutes */ }, cancellationToken);
}

app.MapNode(new FetchRequestHandler(pool, new() { Module = module }), new MyRouteProvider(pool, module));
```

A `RenderRoute` has a URLPattern `Pattern` (`/parks/:parkRef`), an `Id` that names the endpoint, and a `RenderMode`:
`Server`, `Prerender`, or `Client`, which is not mapped. `MapNodeOptions.ConfigureEndpoint` sees each endpoint, for
caching or authorization by route; `FallbackPattern` is the fallback, or null for none.

`INodeRequestHandler` (`PrepareAsync`, `HandleAsync`) is the seam for an application whose server protocol is not a
`fetch` handler.

## Constraints

- Set `EngineCount` to the cores the process may use; a container reports the host's.
- Modules are CommonJS, bundled, without code splitting. A rebuilt bundle needs a restart.
- Module state is shared by every render on an engine.
- A streamed response cannot change its status after its first byte.
