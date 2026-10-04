# Alethic.Node

Node.js embedded in a .NET process, through [node-api-dotnet](https://github.com/microsoft/node-api-dotnet): a pool
of real Node runtimes runs JavaScript inside your application, with no child process in between. On top of it,
serving a JavaScript application's pages from ASP.NET Core or ASP.NET, and hosting JavaScript components on Web Forms
pages. .NET 8 and 9, and .NET Framework 4.7.2 and later.

| Package | For |
| --- | --- |
| [Alethic.Node](src/Alethic.Node/README.md) | Running JavaScript in any .NET process. |
| [Alethic.Node.AspNetCore](src/Alethic.Node.AspNetCore/README.md) | Serving an application's pages from ASP.NET Core. |
| [Alethic.Node.AspNet](src/Alethic.Node.AspNet/README.md) | Running JavaScript in an ASP.NET (System.Web) site, and serving an application's pages from it. |
| [Alethic.Node.AspNet.Components](src/Alethic.Node.AspNet.Components/README.md) | React, or any framework's, components on Web Forms pages. |
| [Alethic.Node.Http](src/Alethic.Node.Http/README.md) | The protocol the two web hosts share. Referenced through them. |

Each needs the native runtime for the platforms you deploy to: `Microsoft.JavaScript.LibNode.<rid>`.

```csharp
services.AddNodeEnginePool(o => o.EngineCount = 2);

var pool = provider.GetRequiredService<NodeEnginePool>();
var slug = await pool.RunAsync(NodeModuleSource.FromFile("tools.cjs"), exports =>
    Task.FromResult((string)exports.CallMethod("slugify", "Enchanted Rock")));
```

## Samples

- [Sample.Console](samples/Sample.Console): the pool calling a JavaScript module.
- [Sample.Load](samples/Sample.Load): an adaptive pool under a changing workload, its decisions checked.
- [Sample.React](samples/Sample.React): a React application served from ASP.NET Core, an endpoint per route.
- [Sample.WebForms](samples/Sample.WebForms): React components on Web Forms pages, and the application's pages beside them.
- [Sample.Client](samples/Sample.Client): the React client the web samples share.

## Building

`dotnet build Alethic.Node.slnx` with the .NET 9 SDK; the .NET Framework projects need Windows. The tests are
executables under each test project's `bin`. The client needs Yarn, through `corepack enable`.
