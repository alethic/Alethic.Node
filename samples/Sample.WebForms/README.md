# Sample.WebForms

An ASP.NET Web Forms site (.NET Framework 4.8) that hosts React components with `NodeComponent` from
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

- **`Client`** is the React client. `npm run build` writes the two bundles where `NodeComponent` looks by default:
  - `Server/components/client.js`, the browser entry, exporting `outlet` and the components;
  - `Server/App_Data/components/server.cjs`, the server bundle, exporting `renderOutlets`.

  `src/outlets.tsx` places every component in one root through portals, and `src/render.tsx` renders a page's
  components with React's `prerender`. Both follow the contract in Alethic.Node.AspNet.Components' README.
- **`Server`** is the site. It is an SDK-style project whose code-behind compiles to `bin\`, beside the pages. Building it
  builds the client first (`/p:BuildClient=false` skips that). Cogito.AspNet.MSBuild writes the binding redirects into
  `Web.config`. Nothing is configured for the components: every default fits.

## Running it

Build the site, then serve its folder with IIS Express, 64-bit since libnode is:

```bat
dotnet build samples\Sample.WebForms\Server
"C:\Program Files\IIS Express\iisexpress.exe" /path:%CD%\samples\Sample.WebForms\Server /port:8090
```

and open `http://localhost:8090/`.

Node starts once per process. Rebuilding the site makes ASP.NET start it again in a new AppDomain of the same process,
where Node cannot start, so restart IIS Express after each build.
