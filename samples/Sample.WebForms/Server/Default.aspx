<%@ Page Title="Start" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="Sample.WebForms.Default" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Node components on Web Forms</h1>
    <p>
        Each page hosts components of a React client with the <code>Component</code> control, rendered first on the server, on Node
        inside the worker process, and then in the browser.
    </p>

    <ul>
        <li><a href="/Props.aspx">Props</a>: every way to declare them in markup, bind them, and change them from code; full postbacks.</li>
        <li><a href="/Partial.aspx">Partial postbacks</a>: components inside and outside an <code>UpdatePanel</code>.</li>
        <li><a href="/Async.aspx">Async page</a>: the server render as an async page task, and a command answered asynchronously.</li>
        <li><a href="/Sync.aspx">Sync page</a>: the same, on a page that is not asynchronous.</li>
        <li><a href="/Errors.aspx">Errors</a>: what becomes of a component that fails on the server.</li>
        <li><a href="/Globals.aspx">Global scope</a>: a component with no module, from a plain script's globals.</li>
        <li><a href="/Demo.aspx">Demo</a>: a catalog in a <code>Repeater</code>, with a cart the header's badge shares.</li>
    </ul>

    <h2>The simplest component</h2>
    <p>One prop, and a callback the page answers. The page has a <code>ScriptManager</code>, so the command posts back
        partially and its answer comes back to the component.</p>

    <node:Component ID="Hello" runat="server" Name="Greeting" OnCommand="Hello_Command">
        <node:ComponentProp Name="name" Value="Ada" />
        <node:ComponentCallback Name="onGreet" CommandName="Greet" />
    </node:Component>
</asp:Content>
