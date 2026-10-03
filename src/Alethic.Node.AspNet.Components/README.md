# Alethic.Node.AspNet.Components

Lets an ASP.NET Web Forms page host JavaScript components — React, or any other framework — through a
`Component` control. The control behaves like any other control on the page:
- its props are set in markup and changed from code;
- the props are kept in view state;
- its callbacks raise server commands;
- it renders on the server, on Node embedded in the worker process through
  [Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet).

A control names a component by two things: the **module** it is in, and its **name** within that module. Each side has
its own module, because each side has its own build:
- **`Module`**, for the browser: an ES module that exports `outlet` and the components.
- **`ServerModule`**, for the server: a CommonJS file that exports `renderOutlets` and the same components.

Both follow the contract under [The modules](#the-modules).

## On a page

```aspx
<%@ Register Assembly="Alethic.Node.AspNet.Components" Namespace="Alethic.Node.AspNet.Components" TagPrefix="node" %>

<node:Component ID="rcPanel" runat="server" OnCommand="rcPanel_Command"
    Module="~/client/client.js" ServerModule="~/App_Data/client/server.cjs" Name="GreetingPanel">
    <node:ComponentProp Name="title" Value="Hello" />
    <node:ComponentProp Name="limit" Value="5" Type="Number" />
    <node:ComponentProp Name="filter">
        <node:ComponentProp Name="brand" Value="Eppendorf" />
    </node:ComponentProp>
    <node:ComponentProp Name="columns">
        <node:ComponentProp Value="sku" />
        <node:ComponentProp Value="name" />
    </node:ComponentProp>
    <node:ComponentCallback Name="onGreeted" CommandName="Greeted" />
</node:Component>
```

Nested props need no `runat="server"`, as a list's items need none. From code:

```csharp
rcPanel.Props["title"] = "Pipettes";
rcPanel.Props["onSelect"] = new ComponentCommand("Select");
rcPanel.Props.Remove("limit");          // the component's default applies
```

A prop is kept in view state only where code changed it from what the markup declared, and like any other control's
state, `EnableViewState="false"` turns that off.

## Commands

A callback prop reaches the component as a function. Calling it raises the control's `Command` event, the way a
button's `CommandName` raises its container's command. The command then bubbles, so a `Repeater` raises it as an item
command.

```csharp
protected void rcPanel_Command(object sender, ComponentCommandEventArgs e)
{
    switch (e.CommandName)
    {
        case "Greeted":
            e.Result = SaveAsync(e.Argument<string>(0));     // the callback's promise resolves to this
            break;
    }
}
```

- **The result:** `e.Result` is what the callback's promise resolves to. It may be any value that serializes, or a
  `Task` of one. If the handler throws, the promise rejects.
- **Where the command is raised:**
  - **In the browser:** the command posts back. Inside an `UpdatePanel`, or on any page with a `ScriptManager`, the
    postback is partial and the result comes back with it.
  - **During the server render:** the command is raised there and then, in the page's own request.
- **Asynchronous results** need the page to be `Async="true"`.
- **Before posting back:** `OnClientCommand` names a JavaScript function that sees each command in the browser first.
  If it returns `false`, the command doesn't post back.

## Properties

Beside `Props` and `OnClientCommand`:

| Property | |
| --- | --- |
| `Module` | The browser's module. Required. A `~/` path must be there, and is stamped with its write time; any other URL or specifier is imported as it is. |
| `ServerModule` | The server's module, `require`d by Node in the worker process. A `~/` or absolute path, which must be there. Without it the component renders only in the browser. |
| `Name` | The component: an export of the module, or a dotted path through one, as `Catalog.ProductCard`. Found in `Module` in the browser and in `ServerModule` on the server. |
| `ServerRender` | Whether this component renders on the server, where there is a `ServerModule`. `true` unless set. |
| `ServerRenderTimeout` | How long the page waits for its server render. Ten seconds unless set. |
| `ScriptAttributes` | Attributes for the inline script that places the component, for sites whose filters rewrite inline scripts. |

The control assumes nothing about where a site keeps its JavaScript. To set the modules for every control on the site,
use a skin in the site's theme, set as a style sheet theme so a page's own values win:

```aspx
<node:Component runat="server" Module="~/client/client.js" ServerModule="~/App_Data/client/server.cjs" />
```

The page links the module's stylesheet, if it has one, as it links any other. The pool is `AspNetNode.Pool` (see
Alethic.Node.AspNet).

## Server rendering

Where a control has a `ServerModule`, its component renders once `PreRender` is complete, in one call to that module's
`renderOutlets` for all the components on the page that share it. Each control then sends its component's HTML inside
its element. The browser shows that HTML until the component has rendered there. Whether it then replaces that HTML or
hydrates it is the module's choice.

A component's `fetch` of the site is answered in process, by the site's own handler, as the visitor (see
Alethic.Node.AspNet).

Nothing that fails is passed over:
- A component that throws, or that rejects one of its callbacks' promises without catching it, makes its control throw
  a `ComponentRenderException` from its render. A command handler's exception becomes the inner exception.
- A `Name` the module has nothing at fails that component the same way.
- A render that fails as a whole, or that times out, fails the page.
- A server module that is rebuilt while the site runs is not picked up until the application pool recycles.

## The modules

The control finds the component in the module, then hands it to one of two functions the module exports. How they render
is up to the module: which framework, one root per component or one for the page, replacing the server's HTML or
hydrating it. The examples here use React.

Each module is self-contained, as far as the control is concerned. Components from different modules come from
different builds, and share nothing their modules don't share, React included. That is the modules' author's to arrange
where it is wanted.

### `outlet(component, element, props)`

Exported by the browser's module.

- **`component`** is what `Name` found in the module.
- **`element`** is the control's element. It holds the server's HTML where the component rendered on the server.
- **`props`** are the component's props. Each callback is already a function returning a promise of the command's
  result; pass it through.
- **It returns** a function that removes the component. The control never calls it.

The control calls `outlet` from a script it registers with the page's `ScriptManager` where there is one, so it calls
it again for the same element id after every partial postback that renders the control. Noticing that an element has
left the page, and unmounting what was in it, is the module's job.

```tsx
import { createRoot } from "react-dom/client";

export * from "./components";

export function outlet(Component, element, props) {
    const root = createRoot(element);
    root.render(<Component {...props} />);
    return () => root.unmount();
}
```

### `renderOutlets(requests)`

Exported by the server's module, which runs on Node embedded in the worker process. That runtime cannot `import()`, so
the module is CommonJS. Node's `require` resolves what it requires as it would for any program, so a module that
bundles its dependencies needs nothing beside it. A bundled module also needs `process.env.NODE_ENV` defined.

- **`requests`** is every component on the page that renders on the server with this module: `[{ id, component, props }]`,
  where `component` is what `Name` found in the module. Each callback in the props is a function returning a promise,
  which raises the command on the page there and then.
- **It resolves to** JSON naming what became of every component, by `id`: `{ html }`, or
  `{ error: { message, stack, componentStack, dotnetErrorId } }`. A component it says nothing of fails the page.
- **A command whose handler threw** rejects with an `Error` carrying `dotnetErrorId`. Returning that id in the
  component's error makes the handler's exception the inner exception of the control's `ComponentRenderException`.

A `fetch` of the site made while `renderOutlets` runs, to a relative URL or the page's own origin, is answered in
process by the site's own handler, as the visitor.

```tsx
import { prerender } from "react-dom/static";

export * from "./components";

export async function renderOutlets(requests) {
    const rendered = {};
    for (const { id, component: Component, props } of requests) {
        let error = null;
        try {
            const { prelude } = await prerender(<Component {...props} />, {
                onError: (e, info) => { error ??= describe(e, info?.componentStack); },
            });
            const html = await new Response(prelude).text();
            rendered[id] = error ? { error } : { html };
        } catch (e) {
            rendered[id] = { error: error ?? describe(e) };
        }
    }
    return JSON.stringify(rendered);
}

function describe(e, componentStack) {
    return { message: e?.message ?? String(e), stack: e?.stack, componentStack, dotnetErrorId: e?.dotnetErrorId };
}
```

The control trusts what `renderOutlets` reports, so failures are failures only as far as the module reports them.
React, for one, recovers from some errors by leaving a component to the browser, and tells only `onError`, which the
example reports. A stricter module also waits for the commands each component called and fails a component that left
one's rejection unhandled: wrap each callback, mark the rejections that pass through it as that component's, and check
Node's `unhandledRejection` reports for them before answering.
