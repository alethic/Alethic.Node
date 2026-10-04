# Alethic.Node

Node.js embedded in a .NET process. A pool of real Node runtimes (libnode, through
[node-api-dotnet](https://github.com/microsoft/node-api-dotnet)) runs JavaScript modules inside your application, with
no child process and no socket between .NET and JavaScript. On top of the pool, packages for serving a JavaScript
application's pages from ASP.NET Core or ASP.NET (System.Web), and for hosting JavaScript components on Web Forms
pages.

Runs on .NET 8 and 9, and on .NET Framework 4.7.2 and later.

## Which package

| You want to | Package |
| --- | --- |
| Run JavaScript in any .NET process: a console, a service, a job | [Alethic.Node](src/Alethic.Node/README.md) |
| Serve a JavaScript application's pages from ASP.NET Core | [Alethic.Node.AspNetCore](src/Alethic.Node.AspNetCore/README.md) |
| Run JavaScript in an ASP.NET (System.Web) site, or serve an application's pages from it | [Alethic.Node.AspNet](src/Alethic.Node.AspNet/README.md) |
| Put React (or any framework's) components on Web Forms pages | [Alethic.Node.AspNet.Components](src/Alethic.Node.AspNet.Components/README.md) |

[Alethic.Node.Http](src/Alethic.Node.Http/README.md) holds the protocol the two web hosts share for serving an
application, so one application is served unchanged by either; you reference it only through them.

Every package needs the native Node runtime for each platform you run on: `Microsoft.JavaScript.LibNode.win-x64`,
`linux-x64`, `linux-arm64`, `osx-x64` or `osx-arm64`. Reference the ones you deploy to, not the umbrella package,
which carries all of them.

## In short

```csharp
services.AddNodeEnginePool(o => o.EngineCount = 2);

var pool = provider.GetRequiredService<NodeEnginePool>();
var module = NodeModuleSource.FromFile("tools.cjs");

var slug = await pool.RunAsync(module, exports =>
    Task.FromResult((string)exports.CallMethod("slugify", "Enchanted Rock")));
```

The pool starts engines as they are needed, loads the module once on each with Node's own `require`, and runs the work
on the engine's thread, where the JavaScript values are. Everything else in the repository is a way of putting a web
request in front of that.

Serving an application's pages from ASP.NET Core:

```csharp
builder.Services.AddNodeEnginePool(o => o.EngineCount = 2);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapNodeFetchHandler(o => o.Module = NodeModuleSource.FromFile("ssr/app.cjs"));
app.Run();
```

where `app.cjs` is a bundle whose default export has a `fetch(request, env, ctx)` function returning a `Response`, the
shape most server-rendering frameworks build to.

## Samples

| Sample | What it shows |
| --- | --- |
| [Sample.Console](samples/Sample.Console) | The pool on its own: a console application calling a JavaScript module. |
| [Sample.Load](samples/Sample.Load) | An adaptive pool under a changing workload, watched as it learns, with its decisions checked. |
| [Sample.React](samples/Sample.React) | A React application served from ASP.NET Core: server rendering, hydration, and an endpoint per route read from the application's own router. |
| [Sample.WebForms](samples/Sample.WebForms) | React components on Web Forms pages, every way a page can drive them, and the same application's pages served from System.Web routes. |
| [Sample.Client](samples/Sample.Client) | The React client the two web samples share, built with Vite into each sample's bundles. |

## Building

The solution is `Alethic.Node.slnx`. It needs the .NET 9 SDK, and for the Web Forms sample and the .NET Framework
packages, a Windows machine with the .NET Framework 4.7.2 and 4.8 reference assemblies, which Visual Studio installs.
The client needs Node and Yarn, which Corepack provides: `corepack enable` once per machine.

```shell
dotnet build Alethic.Node.slnx
```

The tests are MSTest projects under `src`, one per package, built as executables: run
`src/<Project>/bin/<Configuration>/<tfm>/<Project>.exe` on .NET Framework, or
`dotnet src/<Project>/bin/<Configuration>/<tfm>/<Project>.dll` on .NET. They run real Node engines and need the
`LibNode` package for the machine, which each test project references.

CI builds and tests on Windows, Linux and macOS, and packs to `dist/nuget`.

## Constraints

- **One Node per process.** Node starts once per process and cannot start again, so every pool in a process shares
  one runtime platform. An ASP.NET application restarted in a new AppDomain of the same process cannot start Node
  again until the process is recycled; keep such hosts from restarting applications in place.
- **CommonJS only.** The embedded runtime cannot `import()`, so modules are CommonJS, bundled with their
  dependencies and without code splitting.
- **Module scope is per engine, and shared by everything the engine runs.** A module loads once per engine and keeps
  its state for the engine's life; a rebuilt module is not picked up until the host restarts.
- **An engine's memory is V8's.** The .NET garbage collector neither manages nor sees it. The pool reports each
  engine's heap, and an adaptive pool stops growing when the machine's memory is scarce.
