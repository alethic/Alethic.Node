# Sample.React

A React 19 application served from ASP.NET Core: pages rendered on an embedded Node engine with their data in the
markup, hydrated in the browser, with an endpoint per route read from the application's own router.

```shell
dotnet run --project samples/Sample.React/Server
```

`Program.cs` registers the pool, builds a `FetchRequestHandler` over the client's `ssr/app.cjs`, and maps it with
`SampleRouteProvider`, which reads the `router` the module exports. The client is
[`samples/Sample.Client`](../Sample.Client), built and copied as the server builds.
