using System;
using System.Threading.Tasks;
using System.Web.UI;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// A page that is <c>Async="true"</c>.
/// </summary>
public partial class AsyncPage : Page
{

    /// <summary>
    /// Answers the greeting with a task.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The command.</param>
    protected void Slow_Command(object sender, ComponentCommandEventArgs e)
    {
        e.Result = GreetAsync(e.Argument<string>(0));
    }

    /// <summary>
    /// Takes its time over a greeting.
    /// </summary>
    /// <param name="name">Who to greet.</param>
    static async Task<string> GreetAsync(string? name)
    {
        var started = DateTime.Now;
        await Task.Delay(750);
        return $"Hello back, {name}: the page awaited from {started:T} to {DateTime.Now:T}.";
    }

}
