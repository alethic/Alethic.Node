# Sample.WebForms

A Web Forms site (.NET Framework 4.8) hosting React components with the `Component` control, a page per way a page
can drive them, and the shared client's application served from routes beside the pages.

```shell
dotnet run --project samples/Sample.WebForms/Server
```

Serves with IIS Express at `http://localhost:8090/`. Restart it after a rebuild: Node cannot start twice in a process.

| Page | |
| --- | --- |
| `Default.aspx` | One prop, one callback, a partial postback. |
| `Props.aspx` | Every kind of prop: markup, bound, from code; full postbacks. |
| `Partial.aspx` | Components in and out of an `UpdatePanel`; the one inside keeps its state. |
| `Async.aspx`, `Sync.aspx` | The server render on an async page and a synchronous one; an in-process `fetch`; a `Task` result. |
| `Errors.aspx` | A component and a command handler that throw. |
| `Globals.aspx` | A component from a plain script, no module, no framework. |
| `Demo.aspx` | A catalog in a `Repeater`, commands bubbling to `ItemCommand`, a cart shared with the master page. |
| `/about`, `/parks/{parkRef}` | Routes, not pages: the application's own documents, from `Global.asax`. |

`App_Themes/Site/Component.skin` points every control at the client's bundles, which the build copies from
[`samples/Sample.Client`](../Sample.Client).
