<%@ Page Title="Props" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Props.aspx.cs" Inherits="Sample.WebForms.PropsPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <h1>Props</h1>
    <p>
        No <code>ScriptManager</code> here: a command posts the whole page back, as a button does, and the page answers by
        rendering again. Each component shows its props as JavaScript sees them.
    </p>

    <h2>Declared in markup</h2>
    <p>Text is a string unless <code>Type</code> says otherwise. Nested props make an object where they have names, and an
        array where they have none.</p>
    <node:NodeComponent ID="Declared" runat="server" Component="PropsView" OnCommand="Any_Command">
        <node:ComponentProp Name="title" Value="Pipettes" />
        <node:ComponentProp Name="limit" Value="25" Type="Number" />
        <node:ComponentProp Name="ratio" Value="0.75" Type="Number" />
        <node:ComponentProp Name="inStock" Value="true" Type="Boolean" />
        <node:ComponentProp Name="note" />
        <node:ComponentProp Name="filter">
            <node:ComponentProp Name="brand" Value="Eppendorf" />
            <node:ComponentProp Name="volume">
                <node:ComponentProp Name="min" Value="10" Type="Number" />
                <node:ComponentProp Name="max" Value="1000" Type="Number" />
            </node:ComponentProp>
        </node:ComponentProp>
        <node:ComponentProp Name="columns">
            <node:ComponentProp Value="sku" />
            <node:ComponentProp Value="name" />
            <node:ComponentProp Value="price" />
        </node:ComponentProp>
        <node:ComponentProp Name="rows">
            <node:ComponentProp>
                <node:ComponentProp Name="sku" Value="PIP-100" />
                <node:ComponentProp Name="qty" Value="2" Type="Number" />
            </node:ComponentProp>
            <node:ComponentProp>
                <node:ComponentProp Name="sku" Value="TIP-200" />
                <node:ComponentProp Name="qty" Value="10" Type="Number" />
            </node:ComponentProp>
        </node:ComponentProp>
        <node:ComponentCallback Name="onPing" CommandName="Ping" />
    </node:NodeComponent>

    <h2>Bound</h2>
    <p>A data-binding expression keeps its value's own type: a number, a date, a list, an object.</p>
    <node:NodeComponent ID="Bound" runat="server" Component="PropsView" OnCommand="Any_Command">
        <node:ComponentProp Name="lines" Value='<%# Order.Lines %>' />
        <node:ComponentProp Name="total" Value='<%# Order.Total %>' />
        <node:ComponentProp Name="placed" Value='<%# Order.Placed %>' />
        <node:ComponentProp Name="tags" Value='<%# Order.Tags %>' />
        <node:ComponentProp Name="shipTo" Value='<%# Order.ShipTo %>' />
        <node:ComponentProp Name="totalAsText" Value='<%# Order.Total %>' Type="String" />
    </node:NodeComponent>

    <h2>Changed from code</h2>
    <p>Props code changed are kept in view state, so they last across postbacks as any control's properties do.</p>
    <node:NodeComponent ID="FromCode" runat="server" Component="PropsView" OnCommand="Any_Command">
        <node:ComponentProp Name="clicks" Value="0" Type="Number" />
    </node:NodeComponent>
    <asp:Button ID="Click" runat="server" Text="Count a click, from code" OnClick="Click_Click" />

    <h2>What the page heard</h2>
    <asp:Literal ID="Log" runat="server" Mode="Encode" Text="Nothing yet." />
</asp:Content>
