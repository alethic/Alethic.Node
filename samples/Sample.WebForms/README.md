# Sample.WebForms

An ASP.NET Web Forms site (.NET Framework 4.8) that hosts React components with the `Component` control from
Alethic.Node.AspNet.Components. Each page shows one of the ways Web Forms can drive a component.

| Page | What it shows |
| --- | --- |
| `Default.aspx` | The simplest component: one prop, and a callback the page answers through a partial postback. |
| `Props.aspx` | Props declared in markup with every type and nesting, bound with `<%# %>` keeping their types, and changed from code and kept in view state. There is no `ScriptManager`, so commands are full postbacks. |
| `Partial.aspx` | Components inside and outside an `UpdatePanel`: which are placed again by a partial postback, and which keep their state. |
| `Async.aspx` | An `Async="true"` page: the server render as an async page task, an in-process `fetch` of `Time.ashx` as the visitor, and a command answered with a `Task`. |
| `Sync.aspx` | The same on a page that is not asynchronous: the server render blocks, and a command answered with a `Task` fails, telling the component why. |
| `Errors.aspx` | A component that throws, or whose command's handler throws, failing the page with a `ComponentRenderException`. |
| `Demo.aspx` | A catalog in a `Repeater` inside an `UpdatePanel`. Products are bound to their components, commands bubble to `ItemCommand`, and a cart is shared with the master page's badge through the page's single React tree. |

## Layout

- **`Server`** is the site. It is an SDK-style project whose code-behind compiles to `bin\`, beside the pages.
  Cogito.AspNet.MSBuild writes the binding redirects into `Web.config`. Nothing is configured for the components: every
  default fits.
- **The client** is `samples/Sample.Client`, which the ASP.NET Core sample shares. The site references its project, and
  building the site builds it and copies its two Web Forms bundles where `Component` looks by default:
  - `Server/components/client.js`, the browser entry, exporting `outlet` and the components;
  - `Server/App_Data/components/server.cjs`, the server bundle, exporting `renderOutlets`.

## Running it

Run the project, which serves the site's folder with 64-bit IIS Express, as libnode is, on port 8090:

```bat
dotnet run --project samples\Sample.WebForms\Server
```

From Visual Studio, start it as any project. Then open `http://localhost:8090/`. Pass `-p:IISExpressPort=…` for another
port.

Node starts once per process. Rebuilding the site makes ASP.NET start it again in a new AppDomain of the same process,
where Node cannot start, so restart IIS Express after each build.
