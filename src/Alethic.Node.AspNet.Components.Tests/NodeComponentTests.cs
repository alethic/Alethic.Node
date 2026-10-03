using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web.UI;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Components.Tests;

/// <summary>
/// The control apart from a page: its props from markup, its view state, and its commands.
/// </summary>
[TestClass]
public class NodeComponentTests
{

    /// <summary>
    /// The control with its protected lifecycle exposed.
    /// </summary>
    sealed class Exposed : NodeComponent
    {

        /// <summary>
        /// Initializes it, which builds its props from the markup.
        /// </summary>
        public new void Init() => OnInit(EventArgs.Empty);

        /// <summary>
        /// Saves its view state.
        /// </summary>
        public object? Save() => SaveViewState();

        /// <summary>
        /// Loads view state.
        /// </summary>
        /// <param name="state">The state.</param>
        public new void Load(object? state) => LoadViewState(state);

    }

    /// <summary>
    /// A control with props declared as the markup would: a string, JSON, an object, an array, and callbacks.
    /// </summary>
    static Exposed Declared()
    {
        var control = new Exposed() { ID = "rc", Component = "Panel" };
        control.Controls.Add(new ComponentProp() { Name = "title", Value = "Pipettes" });
        control.Controls.Add(new ComponentProp() { Name = "count", Value = "3", Type = ComponentPropType.Number });

        var filter = new ComponentProp() { Name = "filter" };
        filter.Controls.Add(new ComponentProp() { Name = "brand", Value = "Eppendorf" });
        control.Controls.Add(filter);

        var columns = new ComponentProp() { Name = "columns" };
        columns.Controls.Add(new ComponentProp() { Value = "sku" });
        columns.Controls.Add(new ComponentProp() { Value = "name" });
        control.Controls.Add(columns);

        control.Controls.Add(new ComponentCallback() { Name = "onSelect", CommandName = "Select" });
        control.Controls.Add(new LiteralControl("  "));
        control.Init();
        return control;
    }

    /// <summary>
    /// The markup's props build the control's.
    /// </summary>
    [TestMethod]
    public void Markup_builds_the_props()
    {
        Assert.AreEqual("""{"title":"Pipettes","count":3,"filter":{"brand":"Eppendorf"},"columns":["sku","name"],"onSelect":{"$componentCommand":"Select"}}""", Declared().Props.ToJson());
    }

    /// <summary>
    /// A prop without a name cannot be built.
    /// </summary>
    [TestMethod]
    public void A_prop_needs_a_name()
    {
        var control = new Exposed();
        control.Controls.Add(new ComponentProp() { Value = "x" });

        Assert.ThrowsExactly<InvalidOperationException>(control.Init);
    }

    /// <summary>
    /// Props the markup declared take no view state; props code changed are kept in it, and come back.
    /// </summary>
    [TestMethod]
    public void Only_changed_props_take_view_state()
    {
        var control = Declared();
        Assert.IsNull(control.Save());

        control.Props["title"] = "Tips";
        control.Props.Remove("count");
        var state = control.Save();
        Assert.IsNotNull(state);

        var next = Declared();
        next.Load(state);
        Assert.AreEqual(control.Props.ToJson(), next.Props.ToJson());
    }

    /// <summary>
    /// A command raises the control's <see cref="NodeComponent.Command"/>, with its arguments.
    /// </summary>
    [TestMethod]
    public void A_command_raises_the_command_event()
    {
        var control = Declared();
        ComponentCommandEventArgs? raised = null;
        control.Command += (sender, e) => raised = e;

        control.RaiseCommand("Select", [JsonSerializer.SerializeToElement(new { sku = "A1" })], false);

        Assert.IsNotNull(raised);
        Assert.AreEqual("Select", raised.CommandName);
        Assert.AreEqual("A1", raised.Argument<Selection>(0)!.Sku);
        Assert.IsNull(raised.Argument<Selection>(1));
        Assert.IsFalse(raised.IsServerRender);
    }

    /// <summary>
    /// A command the props do not hold cannot be raised: the name is the only part of a postback the page checks.
    /// </summary>
    [TestMethod]
    public void Only_a_command_the_props_hold_is_raised()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Declared().RaiseCommand("Delete", [], false));
    }

    /// <summary>
    /// During a server render, a command's result is its JSON, awaited where it is a task.
    /// </summary>
    [TestMethod]
    public async Task A_server_command_answers_with_its_result()
    {
        var control = Declared();
        control.Command += (sender, e) => e.Result = Answer();

        static async Task<object> Answer()
        {
            await Task.Delay(1);
            return new { Done = true };
        }

        Assert.AreEqual("""{"done":true}""", await control.Outlet(true).Raise("Select", []));
    }

    /// <summary>
    /// On a page that is not asynchronous, a command that answers asynchronously during a server render fails.
    /// </summary>
    [TestMethod]
    public void A_synchronous_page_refuses_an_asynchronous_answer()
    {
        var control = Declared();
        control.Command += (sender, e) => e.Result = Task.Delay(100);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => control.Outlet(false).Raise("Select", []));
        StringAssert.Contains(thrown.Message, "Async=\"true\"");
    }

    /// <summary>
    /// What a command's arguments are read as.
    /// </summary>
    sealed class Selection
    {

        /// <summary>
        /// The SKU selected.
        /// </summary>
        public string? Sku { get; set; }

    }

}
