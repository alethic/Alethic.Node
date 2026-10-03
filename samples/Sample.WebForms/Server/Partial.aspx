<%@ Page Title="Partial postbacks" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Partial.aspx.cs" Inherits="Sample.WebForms.PartialPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Partial postbacks</h1>
    <p>
        With a <code>ScriptManager</code> on the page, a component's command posts back partially, and its answer comes back
        to the component. Count in each component, then post back.
    </p>

    <h2>Inside an UpdatePanel</h2>
    <p>
        A postback the panel takes part in renders it again, and its component is placed again: it gets the page's new
        props and starts its own count over. That is true of its own commands too, since the panel's children trigger it,
        so its answer goes to the panel rather than to a component that is gone.
    </p>
    <asp:UpdatePanel ID="Panel" runat="server">
        <ContentTemplate>
            <node:Component ID="Inside" runat="server" Name="Counter" OnCommand="Inside_Command">
                <node:ComponentProp Name="label" Value="Inside the panel" />
                <node:ComponentCallback Name="onReport" CommandName="Report" />
            </node:Component>
            <asp:Button ID="Refresh" runat="server" Text="Partial postback" OnClick="Refresh_Click" />
            <p class="log">
                The panel rendered at <asp:Literal ID="PanelTime" runat="server" />, after <asp:Literal ID="PostbackCount" runat="server" /> postbacks.
                <asp:Literal ID="InsideLog" runat="server" Mode="Encode" />
            </p>
        </ContentTemplate>
    </asp:UpdatePanel>

    <h2>Outside it</h2>
    <p>
        The panel's postbacks leave this component alone, so it keeps its own count, and the page's count it was given
        stays as it was. Its own commands post back partially too, and their answers come back to it.
    </p>
    <node:Component ID="Outside" runat="server" Name="Counter" OnCommand="Outside_Command">
        <node:ComponentProp Name="label" Value="Outside the panel" />
        <node:ComponentCallback Name="onReport" CommandName="Report" />
    </node:Component>
</asp:Content>
