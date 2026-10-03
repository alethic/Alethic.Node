<%@ Page Title="Sync page" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Sync.aspx.cs" Inherits="Sample.WebForms.SyncPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Sync page</h1>
    <p>
        This page is not asynchronous. Its components render on the server once <code>PreRender</code> is complete, the
        request's thread blocking until they have; it serves what they fetch as they ask, without waiting on the context it
        holds. A command must answer before its handler returns.
    </p>

    <h2>A fetch of the site</h2>
    <node:Component ID="Clock" runat="server" Name="ServerTime" />

    <h2>A command answered at once</h2>
    <node:Component ID="Quick" runat="server" Name="Greeting" OnCommand="Quick_Command">
        <node:ComponentProp Name="name" Value="Ada" />
        <node:ComponentCallback Name="onGreet" CommandName="Greet" />
    </node:Component>

    <h2>A command answered asynchronously</h2>
    <p>The handler answers with a task, which a page that is not asynchronous cannot wait for: the command fails, and the
        component is told why.</p>
    <node:Component ID="Slow" runat="server" Name="Greeting" OnCommand="Slow_Command">
        <node:ComponentProp Name="name" Value="Grace" />
        <node:ComponentCallback Name="onGreet" CommandName="Greet" />
    </node:Component>
</asp:Content>
