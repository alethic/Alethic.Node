# Alethic.Node.AspNet

Node embedded in an ASP.NET (System.Web) application on .NET Framework, over
[Alethic.Node](https://www.nuget.org/packages/Alethic.Node)'s pooled engines. It covers the parts System.Web makes
awkward:
- the application may have no container to keep a pool in;
- the request's `HttpContext` is not on the engine's thread;
- a page's script fetches from the site it is running inside.

## The application's pool

`AspNetNode.Pool` is the application's pool. It works with no setup at all: one engine, made the first time it is
used and disposed of when the application shuts down.

A site with a container supplies its own instead. If `HttpRuntime.WebObjectActivator` supplies a `NodeEnginePool`,
that is the pool, and it is the site's to dispose of.

To change the default pool's settings, declare the `alethic.node` section in `web.config` and set what differs:

```xml
<configSections>
  <section name="alethic.node" type="Alethic.Node.AspNet.NodeSection, Alethic.Node.AspNet" />
</configSections>

<alethic.node engineCount="2" />
```

The section also takes `maxConcurrencyPerEngine` (4), `acquireTimeout` (`00:00:10`), `libNodePath` and
`baseDirectory`. In the two paths, `~/` means the application's root. One engine is a safe default; more engines need
CPU the site is entitled to.

Any pool works with `NodeRequest`: an engine is prepared for requests the first time one calls into it.

Reference `Microsoft.JavaScript.LibNode.win-x64` from the web project. Its build puts `libnode.dll` under
`bin\runtimes\win-x64\native`, which is where it is found.

## Work done for a request

```csharp
var request = new NodeRequest(Context);
var html = await request.RunAsync(module, async exports =>
    (string)await ((JSPromise)request.Call(exports["render"], exports, props)).AsTask());
```

The work runs on an engine's thread, which has no `HttpContext`. When the work needs something done as the request,
it hands that back with `request.InvokeAsync(...)`. The request's thread waits for the work by serving those
hand-backs, so the handed-back code runs with the request's context: `HttpContext.Current`, the page, its controls.

There are two ways to wait:
- **`RunAsync`** waits on the page's synchronization context. Hand-backs may await, and they run alongside one
  another. Use it on a page with `Async="true"`, from a `PageAsyncTask`.
- **`Run`** blocks the thread, for a page that is not asynchronous. That thread holds its synchronization context
  until it is done, so nothing `Run` waits for depends on that context. Each hand-back must finish synchronously; one
  that doesn't throws.

## `fetch` of the site

JavaScript called through `request.Call` can `fetch` the site, and the request answers it in process.

Which `fetch` calls are answered in process:
- a URL relative to the site, such as `fetch('/api/greeting')`;
- an absolute URL on the request's own origin.

Any other URL goes out as a real request.

The in-process answer runs the site's own handler for the path, as the visitor the request is for:
- the handler runs in a context of its own, with that visitor's user, cookies and session;
- the handler is found from the site's `system.webServer/handlers`, and an `.ashx` file is compiled as IIS would;
- `HttpServerUtility.Execute` is not used, because it runs pages and nothing else.

Each `fetch` knows which request it belongs to through an `AsyncLocalStorage`. Requests sharing an engine therefore
never see each other's `fetch` calls, even though they share module scope.

What the child context cannot carry:
- methods other than `GET` and `HEAD` (the `fetch` rejects them);
- request headers other than cookies;
- a request body;
- response headers other than the content type;
- output that isn't text.

## One Node per process

ASP.NET restarts an application in a new AppDomain of the same process, and Node cannot start a second time in one
process. The new AppDomain's pool fails to start an engine until the process is recycled.

Where a site runs Node, keep ASP.NET from restarting applications in place, for example with
`<httpRuntime fcnMode="Disabled" />`. Then recycle the application pool instead.
