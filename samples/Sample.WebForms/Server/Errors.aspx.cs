using System;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// A component that fails, in the way the query string asks.
/// </summary>
public partial class ErrorsPage : Page
{

    /// <summary>
    /// Gives the component its mode.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Page_Load(object sender, EventArgs e)
    {
        Failing.Props["mode"] = Request.QueryString["mode"] ?? "";
    }

    /// <summary>
    /// Fails, as the component asked.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Failing_Command(object sender, ComponentCommandEventArgs e)
    {
        throw new InvalidOperationException("The command's handler failed, as the component asked it to.");
    }

}
