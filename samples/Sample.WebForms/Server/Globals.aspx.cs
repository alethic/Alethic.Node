using System;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// A component from the page's global scope, defined by a plain script.
/// </summary>
public partial class GlobalsPage : Page
{

    /// <summary>
    /// Answers the greeting.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Plain_Command(object sender, ComponentCommandEventArgs e)
    {
        e.Result = $"Hello back, {e.Argument<string>(0)}, to a plain script, at {DateTime.Now:T}.";
    }

}
