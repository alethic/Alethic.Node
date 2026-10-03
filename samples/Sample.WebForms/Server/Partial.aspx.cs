using System;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// Components inside and outside an <see cref="UpdatePanel"/>.
/// </summary>
public partial class PartialPage : Page
{

    /// <summary>
    /// How many postbacks the page has seen, kept in view state.
    /// </summary>
    int Postbacks
    {
        get => ViewState[nameof(Postbacks)] as int? ?? 0;
        set => ViewState[nameof(Postbacks)] = value;
    }

    /// <summary>
    /// Counts a postback the button made.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Refresh_Click(object sender, EventArgs e)
    {
        Postbacks++;
    }

    /// <summary>
    /// Hears the component inside the panel, which the panel's update places again.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Inside_Command(object sender, ComponentCommandEventArgs e)
    {
        Postbacks++;
        InsideLog.Text = $"The component inside reported {e.Argument<int>(0)}.";
        e.Result = "Heard.";
    }

    /// <summary>
    /// Answers the component outside the panel, which is still there to hear it.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Outside_Command(object sender, ComponentCommandEventArgs e)
    {
        Postbacks++;
        e.Result = $"The page heard {e.Argument<int>(0)} at {DateTime.Now:T}, on postback {Postbacks}.";
    }

    /// <summary>
    /// Gives both components the page's count, and the panel its time.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Page_PreRender(object sender, EventArgs e)
    {
        Inside.Props["start"] = Postbacks;
        Outside.Props["start"] = Postbacks;
        PanelTime.Text = DateTime.Now.ToString("T");
        PostbackCount.Text = Postbacks.ToString();
    }

}
