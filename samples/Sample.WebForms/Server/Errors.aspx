<%@ Page Title="Errors" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Errors.aspx.cs" Inherits="Sample.WebForms.ErrorsPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <h1>Errors</h1>
    <p>
        A component that fails while it renders on the server is not passed over: its control throws a
        <code>ComponentRenderException</code> from its render, which reaches the page's error handling as any control's
        failure does. This site turns custom errors off, so you see it. Pick a way to fail:
    </p>
    <ul>
        <li><a href="?mode=throw">throw</a>: the component throws.</li>
        <li><a href="?mode=reject">reject</a>: it calls a command whose handler throws, and leaves the rejection unhandled.
            The handler's exception is the inner exception.</li>
        <li><a href="?mode=await">await</a>: it calls the same command and waits for its answer.</li>
    </ul>

    <node:NodeComponent ID="Failing" runat="server" Component="Failing" OnCommand="Failing_Command">
        <node:ComponentCallback Name="onFail" CommandName="Fail" />
    </node:NodeComponent>
</asp:Content>
