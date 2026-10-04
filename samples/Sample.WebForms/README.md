# Sample.WebForms

An ASP.NET Web Forms site (.NET Framework 4.8) hosting React components with Alethic.Node.AspNet.Components'
`Component` control, one page per way a page can drive them, plus the shared client's full-page application served
from System.Web routes beside the pages.

```shell
dotnet run --project samples/Sample.WebForms/Server
```

That serves the site's folder with 64-bit IIS Express on port 8090 (`-p:IISExpressPort=…` for another); from Visual
Studio, start the project. Then open `http://localhost:8090/`.

Node starts once per process. Rebuilding the site makes ASP.NET restart it in a new AppDomain of the same process,
where Node cannot start, so restart IIS Express after each build.

## The pages

| Page | |
| --- | --- |
| `Default.aspx` | The simplest component: one prop, and a callback the page answers through a partial postback. |
| `Props.aspx` | Props of every type and nesting in markup, bound with `<%# %>` keeping their types, and changed from code and kept in view state. No `ScriptManager`, so commands are full postbacks. |
| `Partial.aspx` | Components inside and outside an `UpdatePanel`: the one inside is updated in place on each partial postback, keeping its state and taking its new props. |
| `Async.aspx` | An `Async="true"` page: the server render as an async page task, a `fetch` of `Time.ashx` answered in process as the visitor, and a command answered with a `Task`. |
| `Sync.aspx` | The same on a page that is not asynchronous: the render blocks, and a command answered with a `Task` fails, saying why. |
| `Errors.aspx` | A component that throws, and one whose command handler throws, each failing the page with a `ComponentRenderException`. |
| `Globals.aspx` | A component with no `Module`: `outlet` and the component are globals a plain script on the page defines, with no framework, and its command is still answered. |
| `Demo.aspx` | A catalog in a `Repeater` inside an `UpdatePanel`: products bound to their components, commands bubbling to `ItemCommand`, and a cart shared with the master page's badge through the page's one React tree. |
| `/about`, `/parks/{parkRef}` | Not pages but routes, mapped in `Global.asax` with `MapNode`: the client's full-page application answers them with whole documents, rendered on Node and hydrated in the browser. |

## Layout

- `Server` is the site: an SDK-style project whose code-behind compiles to `bin\`. Cogito.AspNet.MSBuild writes the
  binding redirects into `Web.config`. `App_Themes/Site/Component.skin` gives every `Component` its `Module` and
  `ServerModule`.
- The client is [`samples/Sample.Client`](../Sample.Client), shared with the ASP.NET Core sample. The site references
  its project, and building the site builds it and copies its bundles where they are served from:
  `components/client.js` and `App_Data/components/server.cjs` for the components, and `app.js` and
  `App_Data/app/app.cjs` for the full-page application.
