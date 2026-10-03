# Alethic.Node.AspNet.React

Lets an ASP.NET Web Forms page host React components through a `ReactComponent` control. The control behaves like any
other control on the page:
- its props are set in markup and changed from code;
- the props are kept in view state;
- its callbacks raise server commands;
- it renders on the server, on Node embedded in the worker process through
  [Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet).

Every component on a page shares one React tree, so whatever context the root provides, each component sees it.

The JavaScript half is the npm package `@alethic/node-aspnet-react`. Your client builds two bundles with it:
- **A browser entry:** an ES module that exports `outlet` and your components.
- **A server bundle:** one self-contained CommonJS file that exports `renderOutlets`.

## On a page

```aspx
<%@ Register Assembly="Alethic.Node.AspNet.React" Namespace="Alethic.Node.AspNet.React" TagPrefix="react" %>

<react:ReactComponent ID="rcPanel" runat="server" Component="GreetingPanel" OnCommand="rcPanel_Command">
    <react:ReactProp Name="title" Value="Hello" />
    <react:ReactProp Name="limit" Json="5" />
    <react:ReactProp Name="filter">
        <react:ReactProp Name="brand" Value="Eppendorf" />
    </react:ReactProp>
    <react:ReactProp Name="columns" Array="true">
        <react:ReactProp Value="sku" />
        <react:ReactProp Value="name" />
    </react:ReactProp>
    <react:ReactCallback Name="onGreeted" CommandName="Greeted" />
</react:ReactComponent>
```

Nested props need no `runat="server"`, as a list's items need none. From code:

```csharp
rcPanel.Props["title"] = "Pipettes";
rcPanel.Props["onSelect"] = new ReactCommand("Select");
rcPanel.Props.Remove("limit");          // the component's default applies
```

A prop is kept in view state only where code changed it from what the markup declared, and like any other control's
state, `EnableViewState="false"` turns that off.

## Commands

A callback prop reaches the component as a function. Calling it raises the control's `Command` event, the way a
button's `CommandName` raises its container's command. The command then bubbles, so a `Repeater` raises it as an item
command.

```csharp
protected void rcPanel_Command(object sender, ReactCommandEventArgs e)
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
| `Alethic:React:Script` | The browser entry, e.g. `~/client/index.js`. A `~/` path is stamped with its write time. |
| `Alethic:React:Stylesheet` | The client's stylesheet, linked once per page. Optional. |
| `Alethic:React:ServerBundle` | The server bundle, e.g. `~/App_Data/react/server.cjs`. Components render on the server only when this is set. |
| `Alethic:React:ServerRenderTimeout` | How long a page waits for its server render. Ten seconds by default. |
| `Alethic:React:ScriptAttributes` | Attributes for the inline script that places each component, for sites whose filters rewrite inline scripts. |

Each setting also has a property on `AspNetReact`, and `AspNetReact.Pool` chooses a pool other than
`AspNetNode.Pool`. A control can opt out of server rendering with `ServerRender="false"`.

## Server rendering

When a server bundle is configured, all of a page's components render in one call once `PreRender` is complete. Each
control then sends its component's HTML inside its element. The browser shows that HTML until the component has
rendered there, and then replaces it. This is not hydration: the page's tree is rendered through portals, and React
does not hydrate a portal.

A component's `fetch` of the site is answered in process, by the site's own handler, as the visitor (see
Alethic.Node.AspNet).

Nothing that fails is passed over:
- A component that throws, or that rejects one of its callbacks' promises without catching it, makes its control throw
  a `ReactRenderException` from its render. A command handler's exception becomes the inner exception.
- A render that fails as a whole, or that times out, fails the page.
- A server bundle that is rebuilt while the site runs is not picked up until the application pool recycles.
