<%@ Page Title="Global scope" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Globals.aspx.cs" Inherits="Sample.WebForms.GlobalsPage" %>

<asp:Content ContentPlaceHolderID="Main" runat="server">
    <asp:ScriptManager runat="server" />

    <h1>Global scope</h1>
    <p>
        A <code>Component</code> with no <code>Module</code> takes the page's global scope as its module: its
        <code>outlet</code> and its component are whatever the page's own scripts put there. Here that is a plain script,
        no module and no framework, and with no <code>ServerModule</code> the component renders only in the browser. Its
        command works as any component's does.
    </p>

    <script>
        // A plain script, not a module: what it defines is in the page's global scope, before the components' scripts run.
        window.outlet = function (component, element, props) {
            element.replaceChildren(component(props));
            return function () { element.replaceChildren(); };
        };

        window.Plain = {
            Greeting: function (props) {
                var text = document.createElement("p");
                text.textContent = "Hello, " + props.name + ", from a plain script.";

                var button = document.createElement("button");
                button.type = "button";
                button.textContent = "Greet the page";
                button.onclick = function () {
                    props.onGreet(props.name).then(function (reply) { text.textContent = reply; });
                };

                var card = document.createElement("div");
                card.className = "card";
                card.append(text, button);
                return card;
            },
        };
    </script>

    <node:Component ID="Plain" runat="server" Module="" ServerModule="" Name="Plain.Greeting" OnCommand="Plain_Command">
        <node:ComponentProp Name="name" Value="Ada" />
        <node:ComponentCallback Name="onGreet" CommandName="Greet" />
    </node:Component>
</asp:Content>
