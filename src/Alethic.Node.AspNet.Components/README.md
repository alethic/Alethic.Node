# Alethic.Node.AspNet.Components

JavaScript components on ASP.NET Web Forms pages, through a `Component` control. React or any other framework: the
control places a component in its element, and the component is one more control on the page.

- Its props are set in markup, bound, or changed from code, and kept in view state.
- Its callbacks raise server commands, which post back, partially inside an `UpdatePanel`.
- It renders on the server first, on Node.js embedded in the worker process through
  [Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet), so the page's HTML carries it.
- A partial postback that renders the control again updates the component in place, so what it holds in the browser
  lives on.

## Install

```shell
dotnet add package Alethic.Node.AspNet.Components
dotnet add package Microsoft.JavaScript.LibNode.win-x64
```

Register the control's prefix in `web.config`, and point every control at your client's two modules with a skin in a
style sheet theme, so that a page's own values win:

```xml
<pages styleSheetTheme="Site">
    <controls>
        <add tagPrefix="node" namespace="Alethic.Node.AspNet.Components" assembly="Alethic.Node.AspNet.Components" />
    </controls>
</pages>
```

```aspx
<%-- App_Themes/Site/Component.skin --%>
<%@ Register Assembly="Alethic.Node.AspNet.Components" Namespace="Alethic.Node.AspNet.Components" TagPrefix="node" %>
<node:Component runat="server" Module="~/client/client.js" ServerModule="~/App_Data/client/server.cjs" />
```

`Module` is the browser's module, an ES module the page imports; `ServerModule` is the server's, a CommonJS bundle Node
requires. Each is a build of your client, and each exports the components and one function, under
[The modules](#the-modules).

## On a page

```aspx
<node:Component ID="rcPanel" runat="server" Name="GreetingPanel" OnCommand="rcPanel_Command">
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

`Name` is the component's export in the modules, or a dotted path to it, as `Catalog.ProductCard`. A prop with
children is an object; one whose children have no names is an array. Nested props need no `runat="server"`. A value
may be bound, `Value='<%# product.Sku %>'`, and keeps its type.

From code:

```csharp
rcPanel.Props["title"] = "Pipettes";
rcPanel.Props["onSelect"] = new ComponentCommand("Select");
rcPanel.Props.Remove("limit");          // the component's own default applies
```

A prop changed from code is kept in view state, as any control property is; `EnableViewState="false"` turns that off.

## Commands

A callback prop reaches the component as a function returning a promise. Calling it raises the control's `Command`
event, as a button's `CommandName` raises its container's, and the command bubbles: in a `Repeater`, to `ItemCommand`.

```csharp
protected void rcPanel_Command(object sender, ComponentCommandEventArgs e)
{
    switch (e.CommandName)
    {
        case "Greeted":
            e.Result = SaveAsync(e.Argument<string>(0));     // what the component's promise resolves to
            break;
    }
}
```

- `e.Arguments` are what the component passed, as JSON; `e.Argument<T>(i)` reads one.
- `e.Result` is what the promise resolves to: any value that serializes, or a `Task` of one, which needs the page to
  be `Async="true"`. A handler that throws rejects the promise.
- In the browser, the command posts back: partially, with the result coming back in the response, where the page has
  a `ScriptManager`. During the server render, it is raised there and then, in the page's own request.
- `OnClientCommand` names a JavaScript function that sees each command first; returning `false` keeps it in the
  browser.

## Server rendering

With a `ServerModule`, each component renders on the server once `PreRender` is done, with the module's `render`, and
its HTML goes inside the control's element. The browser shows that HTML until the component has rendered there.
A `fetch` of the site made while rendering is answered in process, as the visitor, by the site's own handler.

Nothing that fails is passed over. A component that throws, or that leaves one of its callbacks' promises rejected,
makes its control throw a `ComponentRenderException`, with a command handler's exception as the inner exception. A
`Name` the module has nothing at fails the same way. A render that fails as a whole, or times out, fails the page.

| Property | Default | |
| --- | --- | --- |
| `Module` | | The browser's module. A `~/` path must exist, and is stamped with its write time; any other URL or specifier is imported as it is. Unset, the component and `outlet` are read from the page's global scope, where a plain script put them. |
| `ServerModule` | | The server's module, a `~/` or absolute path that must exist. Unset, the component renders only in the browser. |
| `ServerRender` | `true` | Whether this component renders on the server. |
| `ServerRenderTimeout` | 10 s | How long the page waits for its server render. |
| `ScriptAttributes` | | Attributes for the control's inline script, for sites whose filters rewrite inline scripts. |

The browser side is one script, `Components.js`, embedded in the assembly and registered once per page through the
`ScriptManager` where there is one. Each control writes only a call to it. The page links the client's stylesheet, if
it has one, as it links any other.

## The modules

Your client's two builds each export the components and one function. How they render is yours to decide: which
framework, one root per component or one for the page, replacing the server's HTML or hydrating it. These examples
use React.

### Browser: `outlet(component, element, props)`

Places a component in the control's element, which holds the server's HTML where there was a server render. `props`
are ready to use; each callback is already a function returning a promise. It returns a function that removes the
component, or an object with `remove()` and, optionally, `update(element, props)`.

The control is placed again after every partial postback that renders it, in a new element with new props. Where the
module provided `update`, the library calls it instead of removing and placing anew, so the component and its state
live on; where it did not, the component is replaced. A component whose element leaves the page is removed. Nothing
survives a full postback, which loads the page anew: state that should is passed to the page in a callback and comes
back as a prop.

```tsx
import { createRoot } from "react-dom/client";

export * from "./components";

export function outlet(Component, element, props) {
    const container = document.createElement("div");
    element.replaceChildren(container);

    const root = createRoot(container);
    root.render(<Component {...props} />);

    return {
        remove: () => root.unmount(),
        update(next, props) {
            next.replaceChildren(container);
            root.render(<Component {...props} />);
        },
    };
}
```

### Server: `render(component, props, state)`

Renders one component to its HTML, or throws why there is none. `props` are as in the browser, each callback a
function raising the command on the page. `state` is one plain object shared by every component of one page render,
and new for each render: a cache the page's components share goes there. The library calls `render` for each component
on the page in turn, waits for the commands they called, and reports each one's HTML or failure to its control.

The module is CommonJS, bundled with its dependencies (the embedded runtime cannot `import()`), and needs
`process.env.NODE_ENV` defined by the bundler.

```tsx
import { prerender } from "react-dom/static";

export * from "./components";

export async function render(Component, props, state) {
    state.cache ??= new Map();

    let failure;
    const { prelude } = await prerender(<Providers cache={state.cache}><Component {...props} /></Providers>, {
        onError: (e, info) => { failure ??= Object.assign(e, { componentStack: info?.componentStack }); },
    });

    if (failure !== undefined) {
        throw failure;
    }

    return await new Response(prelude).text();
}
```

React recovers from some errors by leaving the component to the browser and tells only `onError`; throwing what it
reported makes those failures too, and a `componentStack` on the error is reported with it.
