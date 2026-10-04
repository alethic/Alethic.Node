# Alethic.Node.AspNet

Node.js in an ASP.NET (System.Web) application on .NET Framework, over
[Alethic.Node](https://www.nuget.org/packages/Alethic.Node)'s pool: a pool that needs no container, work done for a
request from the engine's thread, JavaScript that fetches from the site it runs in, and an application's pages served
from System.Web routes. For components on Web Forms pages, see
[Alethic.Node.AspNet.Components](https://www.nuget.org/packages/Alethic.Node.AspNet.Components).

```shell
dotnet add package Alethic.Node.AspNet
dotnet add package Microsoft.JavaScript.LibNode.win-x64
```

## The pool

`AspNetNode.Pool` works with no setup: one engine. To change it, the `alethic.node` section in `web.config`:

```xml
<configSections>
  <section name="alethic.node" type="Alethic.Node.AspNet.NodeSection, Alethic.Node.AspNet" />
</configSections>

<alethic.node engineCount="2" maxConcurrencyPerEngine="4" acquireTimeout="00:00:10" />
```

A site with a container supplies its own pool, with every option, through `HttpRuntime.WebObjectActivator`.

## Work for a request

```csharp
var request = new NodeRequest(Context);
var html = await request.RunAsync(module, async exports =>
    (string)await ((JSPromise)request.Call(exports["render"], exports, props)).AsTask());
```

The work runs on an engine's thread. What it needs done as the request, with `HttpContext.Current` in place, it hands
back with `request.InvokeAsync(...)`, and the request's thread does it. `RunAsync` is for an `Async="true"` page;
`Run` blocks, for one that is not.

JavaScript called through `request.Call` can `fetch` the site, and the request answers it in process as the visitor,
through the site's own handler for the path: `GET` and `HEAD`, cookies, a text response.

## Pages from a `fetch` handler

```csharp
protected void Application_Start(object sender, EventArgs e)
{
    var app = new FetchRequestHandler(NodeModuleSource.FromFile(HostingEnvironment.MapPath("~/App_Data/app/app.cjs")));
    RouteTable.Routes.MapNode("about", app);
    RouteTable.Routes.MapNode("parks/{parkRef}", app);
}
```

The application is a CommonJS bundle whose default export has a `fetch(request, env, ctx)` function;
[Alethic.Node.Http](https://www.nuget.org/packages/Alethic.Node.Http) says what it sees, and the same bundle serves
from ASP.NET Core. Its response is the site's, status included. Requests no route matches go on to the site's pages;
a catch-all route needs `RouteTable.Routes.Ignore("{resource}.axd/{*pathInfo}")` ahead of it.

## One Node per process

ASP.NET restarts an application in a new AppDomain of the same process, where Node cannot start again. Keep the site
from restarting in place (`<httpRuntime fcnMode="Disabled" />`) and recycle the application pool to deploy.
