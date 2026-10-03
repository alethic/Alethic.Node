using System;
using System.Threading.Tasks;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// A page that is not asynchronous.
/// </summary>
public partial class SyncPage : Page
{

    /// <summary>
    /// Answers at once.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Quick_Command(object sender, ComponentCommandEventArgs e)
    {
        e.Result = $"Hello back, {e.Argument<string>(0)}, at {DateTime.Now:T}.";
    }

    /// <summary>
    /// Answers with a task, which this page cannot wait for.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Slow_Command(object sender, ComponentCommandEventArgs e)
    {
        e.Result = Task.Delay(750).ContinueWith(_ => "Too late.", TaskScheduler.Default);
    }

}
