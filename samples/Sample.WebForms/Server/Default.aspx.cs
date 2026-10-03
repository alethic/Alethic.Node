using System;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// The start page, with the simplest component.
/// </summary>
public partial class Default : Page
{

    /// <summary>
    /// Answers the greeting.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Hello_Command(object sender, ComponentCommandEventArgs e)
    {
        e.Result = $"Hello back, {e.Argument<string>(0)}, from the page at {DateTime.Now:T}.";
    }

}
