<%@ Page Title="Demo" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" CodeBehind="Demo.aspx.cs" Inherits="Sample.WebForms.DemoPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Demo: a catalog</h1>
    <p>
        Each product is a component in a <code>Repeater</code>'s template, its product bound to it. Adding one raises the
        component's command, which its own handler answers with the cart's new count; the component puts the count into
        the page's shared context, and the badge in the header, another component, shows it. The command also bubbles to
        the <code>Repeater</code>'s <code>ItemCommand</code>, as a button's would. Searching posts the panel back and binds
        the <code>Repeater</code> again.
    </p>

    <asp:UpdatePanel ID="Results" runat="server">
        <ContentTemplate>
            <asp:Panel runat="server" DefaultButton="Search">
                <asp:TextBox ID="Term" runat="server" placeholder="pipette, tubes, PIP-100…" />
                <asp:Button ID="Search" runat="server" Text="Search" OnClick="Search_Click" />
            </asp:Panel>

            <div class="grid">
                <asp:Repeater ID="Products" runat="server" OnItemCommand="Products_ItemCommand">
                    <ItemTemplate>
                        <node:Component runat="server" Name="Catalog.ProductCard" OnCommand="Product_Command">
                            <node:ComponentProp Name="product" Value='<%# Container.DataItem %>' />
                            <node:ComponentCallback Name="onAdd" CommandName="Add" />
                        </node:Component>
                    </ItemTemplate>
                </asp:Repeater>
            </div>

            <p class="log"><asp:Literal ID="Log" runat="server" Mode="Encode" /></p>
        </ContentTemplate>
    </asp:UpdatePanel>
</asp:Content>
