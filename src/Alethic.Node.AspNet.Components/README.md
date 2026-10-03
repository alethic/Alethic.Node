# Alethic.Node.AspNet.Components

Lets an ASP.NET Web Forms page host JavaScript components — React, or any other framework — through a
`NodeComponent` control. The control behaves like any other control on the page:
- its props are set in markup and changed from code;
- the props are kept in view state;
- its callbacks raise server commands;
- it renders on the server, on Node embedded in the worker process through
  [Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet).

The JavaScript half is your own client, which builds two bundles to the contract under [The client](#the-client):
- **A browser entry:** an ES module that exports `outlet` and your components.
- **A server bundle:** one self-contained CommonJS file that exports `renderOutlets`.

## On a page

```aspx
<%@ Register Assembly="Alethic.Node.AspNet.Components" Namespace="Alethic.Node.AspNet.Components" TagPrefix="node" %>

<node:NodeComponent ID="rcPanel" runat="server" Component="GreetingPanel" OnCommand="rcPanel_Command">
    <node:ComponentProp Name="title" Value="Hello" />
    <node:ComponentProp Name="limit" Json="5" />
    <node:ComponentProp Name="filter">
        <node:ComponentProp Name="brand" Value="Eppendorf" />
    </node:ComponentProp>
    <node:ComponentProp Name="columns" Array="true">
        <node:ComponentProp Value="sku" />
        <node:ComponentProp Value="name" />
    </node:ComponentProp>
    <node:ComponentCallback Name="onGreeted" CommandName="Greeted" />
</node:NodeComponent>
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

## Settings

| App setting | |
| --- | --- |
| `Alethic:Components:Script` | The browser entry, e.g. `~/client/index.js`. A `~/` path is stamped with its write time. |
| `Alethic:Components:Stylesheet` | The client's stylesheet, linked once per page. Optional. |
| `Alethic:Components:ServerBundle` | The server bundle, e.g. `~/App_Data/react/server.cjs`. Components render on the server only when this is set. |
| `Alethic:Components:ServerRenderTimeout` | How long a page waits for its server render. Ten seconds by default. |
| `Alethic:Components:ScriptAttributes` | Attributes for the inline script that places each component, for sites whose filters rewrite inline scripts. |

Each setting also has a property on `AspNetComponents`, and `AspNetComponents.Pool` chooses a pool other than
`AspNetNode.Pool`. A control can opt out of server rendering with `ServerRender="false"`.

## Server rendering

When a server bundle is configured, all of a page's components render in one call once `PreRender` is complete. Each
control then sends its component's HTML inside its element. The browser shows that HTML until the component has
rendered there. Whether it then replaces that HTML or hydrates it is the client's choice.

A component's `fetch` of the site is answered in process, by the site's own handler, as the visitor (see
Alethic.Node.AspNet).

Nothing that fails is passed over:
- A component that throws, or that rejects one of its callbacks' promises without catching it, makes its control throw
  a `ComponentRenderException` from its render. A command handler's exception becomes the inner exception.
- A render that fails as a whole, or that times out, fails the page.
- A server bundle that is rebuilt while the site runs is not picked up until the application pool recycles.

## The client

The control and your client meet at two functions. How they render is up to the client: which framework, one root per
component or one for the page, replacing the server's HTML or hydrating it. The examples here use React.

### `outlet(component, element, props)`

Exported by the browser entry, beside every component a page may place.

- **`component`** is the export the control names, or `undefined` where the entry exports nothing by that name.
- **`element`** is the control's element. It holds the server's HTML where the component rendered on the server.
- **`props`** are the component's props. Each callback is already a function returning a promise of the command's
  result; pass it through.
- **It returns** a function that removes the component. The control never calls it.

The control calls `outlet` from a script it registers with the page's `ScriptManager` where there is one, so it calls
it again for the same element id after every partial postback that renders the control. Noticing that an element has
left the page, and unmounting what was in it, is the entry's job.

```tsx
import { createRoot } from "react-dom/client";

export { GreetingPanel } from "./GreetingPanel";

export function outlet(Component, element, props) {
    if (Component === undefined) {
        throw new Error("The client exports no such component.");
    }

    const root = createRoot(element);
    root.render(<Component {...props} />);
    return () => root.unmount();
}
```

### `renderOutlets(requests)`

Exported by the server bundle, which runs on Node embedded in the worker process. That runtime resolves nothing but
Node's built-ins, so the bundle is one CommonJS file with every dependency inside it, without `import()`, and with
`process.env.NODE_ENV` defined.

- **`requests`** is every component on the page that renders on the server: `[{ id, component, props }]`, where
  `component` is the name the control was given. Each callback in the props is a function returning a promise, which
  raises the command on the page there and then.
- **It resolves to** JSON naming what became of every component, by `id`: `{ html }`, or
  `{ error: { message, stack, componentStack, dotnetErrorId } }`. A component it says nothing of fails the page.
- **A command whose handler threw** rejects with an `Error` carrying `dotnetErrorId`. Returning that id in the
  component's error makes the handler's exception the inner exception of the control's `ComponentRenderException`.

A `fetch` of the site made while `renderOutlets` runs, to a relative URL or the page's own origin, is answered in
process by the site's own handler, as the visitor.

```tsx
import { prerender } from "react-dom/static";
import * as components from "./components";

export async function renderOutlets(requests) {
    const rendered = {};
    for (const { id, component, props } of requests) {
        const Component = components[component];
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

The control trusts what `renderOutlets` reports, so failures are failures only as far as the client reports them.
React, for one, recovers from some errors by leaving a component to the browser, and tells only `onError`, which the
example reports. A
stricter client also waits for the commands each component called and fails a component that left one's rejection
unhandled: wrap each callback, mark the rejections that pass through it as that component's, and check Node's
`unhandledRejection` reports for them before answering.
