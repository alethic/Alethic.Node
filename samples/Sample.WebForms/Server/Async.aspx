<%@ Page Title="Async page" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" CodeBehind="Async.aspx.cs" Inherits="Sample.WebForms.AsyncPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Async page</h1>
    <p>
        This page is <code>Async="true"</code>. Its components render on the server as an async page task, which the page
        awaits after <code>PreRender</code>, and a command may answer with a <code>Task</code>.
    </p>

    <h2>A fetch of the site</h2>
    <p>
        The component fetches <code>/Time.ashx</code>. On the server the fetch is answered in process, as you, with your
        session; reload and the visit count goes up, the same as when the browser asks.
    </p>
    <node:Component ID="Clock" runat="server" Name="ServerTime" />

    <h2>A command answered asynchronously</h2>
    <p>The handler awaits before it answers; the component's promise waits for it.</p>
    <node:Component ID="Slow" runat="server" Name="Greeting" OnCommand="Slow_Command">
        <node:ComponentProp Name="name" Value="Grace" />
        <node:ComponentCallback Name="onGreet" CommandName="Greet" />
    </node:Component>
</asp:Content>
