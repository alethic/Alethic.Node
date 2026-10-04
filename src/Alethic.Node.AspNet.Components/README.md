# Alethic.Node.AspNet.Components

JavaScript components on ASP.NET Web Forms pages, through a `Component` control: props from markup or code, kept in
view state; callbacks as server commands, posting back partially in an `UpdatePanel`; rendered on the server first,
on Node.js embedded in the worker process ([Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet)).
React or any framework.

```shell
dotnet add package Alethic.Node.AspNet.Components
dotnet add package Microsoft.JavaScript.LibNode.win-x64
```

## Setup

Register the prefix in `web.config`, and give every control your client's two modules with a skin, as a style sheet
theme so a page's own values win:

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

## On a page

```aspx
<node:Component ID="rcPanel" runat="server" Name="GreetingPanel" OnCommand="rcPanel_Command">
    <node:ComponentProp Name="title" Value="Hello" />
    <node:ComponentProp Name="limit" Value="5" Type="Number" />
    <node:ComponentProp Name="filter">
        <node:ComponentProp Name="brand" Value="Eppendorf" />
    </node:ComponentProp>
    <node:ComponentCallback Name="onGreeted" CommandName="Greeted" />
</node:Component>
```

```csharp
rcPanel.Props["title"] = "Pipettes";                        // kept in view state

protected void rcPanel_Command(object sender, ComponentCommandEventArgs e)
{
    if (e.CommandName == "Greeted")
        e.Result = SaveAsync(e.Argument<string>(0));         // what the callback's promise resolves to
}
```

`Name` is the component's export, or a dotted path to it. A prop with named children is an object, with unnamed
ones an array; `Value='<%# … %>'` binds. A callback is a function returning a promise; calling it raises `Command`,
which bubbles as a button's does. A `Task` result needs `Async="true"`. `OnClientCommand` names a script function
that sees the command first and may return `false` to keep it in the browser.

| Property | |
| --- | --- |
| `Module` | The browser's ES module. Unset, the page's global scope. |
| `ServerModule` | The server's CommonJS module. Unset, no server render. |
| `ServerRender`, `ServerRenderTimeout` | `true`; 10 s. |

A component that fails on the server throws a `ComponentRenderException` from its control; a render that fails as a
whole fails the page.

## The modules

Each build exports the components and one function. How they render is yours: these use React.

**Browser: `outlet(component, element, props)`** places the component in the element, which holds the server's HTML,
and returns a remover, or `{ remove(), update(element, props) }`. With `update`, a partial postback that renders the
control again hands the component its new element and props, and its state lives on.

```tsx
export function outlet(Component, element, props) {
    const container = document.createElement("div");
    element.replaceChildren(container);
    const root = createRoot(container);
    root.render(<Component {...props} />);
    return {
        remove: () => root.unmount(),
        update(next, props) { next.replaceChildren(container); root.render(<Component {...props} />); },
    };
}
```

**Server: `render(component, props, state)`** returns the component's HTML or throws. `state` is one object shared by
the page's components, for a cache. A `fetch` of the site is answered in process, as the visitor.

```tsx
export async function render(Component, props, state) {
    let failure;
    const { prelude } = await prerender(<Component {...props} />, { onError: e => { failure ??= e; } });
    if (failure !== undefined) throw failure;
    return await new Response(prelude).text();
}
```
