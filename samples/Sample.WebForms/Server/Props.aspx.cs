using System;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// Props declared, bound and changed from code, on a page whose postbacks are full ones.
/// </summary>
public partial class PropsPage : Page
{

    /// <summary>
    /// What the bound props are bound to.
    /// </summary>
    protected OrderSummary Order { get; } = new(
        3,
        1234.5m,
        new DateTime(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc),
        ["rush", "gift"],
        new Address("1 Lab Way", "Swedesboro", "NJ"));

    /// <summary>
    /// Binds the bound component, and only it: binding a control builds its props from the markup again, which would
    /// undo what code changed on another.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Page_Load(object sender, EventArgs e)
    {
        Bound.DataBind();
    }

    /// <summary>
    /// Counts a click into a prop, from code.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Click_Click(object sender, EventArgs e)
    {
        var clicks = ((ComponentScalar)FromCode.Props["clicks"]!).Get<int>();
        FromCode.Props["clicks"] = clicks + 1;
        Log.Text = $"The button counted click {clicks + 1}.";
    }

    /// <summary>
    /// Hears any component's command. The postback is a full one, so the answer is the page itself.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Any_Command(object sender, ComponentCommandEventArgs e)
    {
        Log.Text = $"{((Control)sender).ID} raised {e.CommandName}({string.Join(", ", e.Arguments)}) at {DateTime.Now:T}.";
    }

}
