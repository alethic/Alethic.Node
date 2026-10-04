# Alethic.Node.AspNet

Node.js in an ASP.NET (System.Web) application on .NET Framework, over
[Alethic.Node](https://www.nuget.org/packages/Alethic.Node)'s pool of embedded engines. It covers what System.Web
makes awkward: a pool with no container to live in, a request whose `HttpContext` is not on the engine's thread, and
JavaScript that fetches from the site it runs inside. It also serves a JavaScript application's pages from System.Web
routes, beside the site's own.

For Web Forms pages that host components, see
[Alethic.Node.AspNet.Components](https://www.nuget.org/packages/Alethic.Node.AspNet.Components), which builds on this.

## Install

```shell
dotnet add package Alethic.Node.AspNet
dotnet add package Microsoft.JavaScript.LibNode.win-x64
```

The `LibNode` package puts `libnode.dll` under `bin\runtimes\win-x64\native`, where the pool finds it in a web
application. A site running under Mono on another system references that system's package instead.

## The pool

`AspNetNode.Pool` is the application's pool. It works with no setup: one engine, started the first time it is used,
disposed of when the application shuts down. To change its settings, declare the `alethic.node` section in
`web.config`:

```xml
<configSections>
  <section name="alethic.node" type="Alethic.Node.AspNet.NodeSection, Alethic.Node.AspNet" />
</configSections>

<alethic.node engineCount="2" maxConcurrencyPerEngine="4" acquireTimeout="00:00:10" />
```

The section also takes `libNodePath` and `baseDirectory`, in which `~/` means the site's root. One engine is a safe
default; more engines need CPU the site is entitled to.

A site with a container supplies its own pool instead, and gets every option the pool has, the adaptive mode included:
if `HttpRuntime.WebObjectActivator` resolves a `NodeEnginePool`, that is the pool, and it is the site's to dispose of.

## Work for a request

```csharp
var request = new NodeRequest(Context);

var html = await request.RunAsync(module, async exports =>
    (string)await ((JSPromise)request.Call(exports["render"], exports, props)).AsTask());
```

The work runs on an engine's thread, which has no `HttpContext`. What it needs done as the request, it hands back with
`request.InvokeAsync(...)`, and the request's own thread does it, with `HttpContext.Current`, the page and its controls
all in place. The request's thread is what waits for the work, by serving those hand-backs.

- **`RunAsync`** waits on the page's synchronization context, so hand-backs may await. For a page with `Async="true"`,
  from a `PageAsyncTask`.
- **`Run`** blocks the thread, for a page that is not asynchronous. Each hand-back must then finish synchronously; one
  that awaits throws.

JavaScript called through `request.Call` can `fetch` the site, and the request answers it in process, as the visitor:
a relative URL, or an absolute one on the request's own origin, runs the site's own handler for that path, found from
`system.webServer/handlers` in `web.config` (an `.ashx` file compiles as IIS would), in a context with the visitor's
user, cookies and session. `GET` and `HEAD` only, with no body and no headers beyond cookies, and a text response.
Any other URL goes out as a real request. Requests sharing an engine never see each other's `fetch` calls.

## Pages from a `fetch` handler

`FetchRequestHandler` serves a JavaScript application's pages, whole: it calls the application's
`fetch(request, env, ctx)` and writes the `Response` back as the site's response. Mount it on routes in
`Application_Start`:

```csharp
protected void Application_Start(object sender, EventArgs e)
{
    var app = new FetchRequestHandler(NodeModuleSource.FromFile(HostingEnvironment.MapPath("~/App_Data/app/app.cjs")));

    RouteTable.Routes.MapNode("about", app);
    RouteTable.Routes.MapNode("parks/{parkRef}", app);
}
```

The application is a self-contained CommonJS bundle, and the protocol is
[Alethic.Node.Http](https://www.nuget.org/packages/Alethic.Node.Http)'s, shared with ASP.NET Core, so the same bundle
serves both; its README says exactly what the application sees. The handler runs as a `NodeRequest`, so the
application's `fetch` of the site is answered in process. The response is the application's, status included: a 404 it
renders is what the visitor sees, not IIS's page. `FetchRequestHandlerOptions` adds `Pool` to the protocol's options,
for a pool other than the application's.

Routes share the URL space with the site: a request no route matches, or for a file that exists, goes on to the site's
pages and handlers. A catch-all route such as `{*path}` would also take `WebResource.axd` and `ScriptResource.axd`;
put `RouteTable.Routes.Ignore("{resource}.axd/{*pathInfo}")` ahead of it. `MapNode` takes any `IHttpHandler` and
returns the `Route`.

## One Node per process

ASP.NET restarts an application in a new AppDomain of the same process, and Node cannot start a second time in one
process, so the restarted application cannot start an engine until the process is recycled. Where a site runs Node,
keep ASP.NET from restarting it in place, with `<httpRuntime fcnMode="Disabled" />` for instance, and recycle the
application pool to deploy.
