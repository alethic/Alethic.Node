using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// A command a <see cref="ReactComponent"/>'s component raised by calling one of its callback props.
/// </summary>
/// <remarks>
/// A <see cref="CommandEventArgs"/>, so it bubbles as a button's command does: a container such as a <c>Repeater</c>
/// raises it as its own item command. Its <see cref="CommandEventArgs.CommandArgument"/> is <see cref="Arguments"/>.
///
/// The arguments are what the component passed, so they are input like any form field: check them before acting on
/// them.
/// </remarks>
public class ReactCommandEventArgs : CommandEventArgs
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="commandName">The name the callback raises.</param>
    /// <param name="arguments">What the component called the callback with.</param>
    /// <param name="isServerRender">Whether the component called it while it was rendered on the server.</param>
    public ReactCommandEventArgs(string commandName, IReadOnlyList<JsonElement>? arguments, bool isServerRender)
        : base(commandName, arguments ?? [])
    {
        Arguments = arguments ?? [];
        IsServerRender = isServerRender;
    }

    /// <summary>
    /// What the component called the callback with.
    /// </summary>
    public IReadOnlyList<JsonElement> Arguments { get; }

    /// <summary>
    /// What the command answers the component with: the value its callback's promise resolves to. Any value
    /// <see cref="ReactValue.FromObject(object)"/> takes, or a <see cref="Task"/> of one, which is awaited — so an
    /// <c>async</c> method's task makes the answer asynchronous. A handler that throws, or a task that fails, rejects the
    /// promise instead.
    /// </summary>
    /// <remarks>
    /// During a server render the answer goes straight back to the component. From the browser it comes back with a
    /// partial postback, through <see cref="ScriptManager.RegisterDataItem(Control, string)"/>; a full postback replaces
    /// the page, and with it the component that asked. Answering asynchronously needs the page to be
    /// <c>Async="true"</c>, so the task can be awaited without blocking the request.
    /// </remarks>
    public object? Result { get; set; }

    /// <summary>
    /// Whether the component called the callback while it was rendered on the server, in this request, rather than in
    /// the browser, which posted it back. React may render a component more than once in one server render, so a
    /// callback called while rendering may be raised more than once.
    /// </summary>
    public bool IsServerRender { get; }

    /// <summary>
    /// One of the arguments, read as a type of the caller's, whose properties match JavaScript's camel-cased ones; the
    /// type's default where the component passed fewer.
    /// </summary>
    /// <typeparam name="T">The type to read it as.</typeparam>
    /// <param name="index">Its position.</param>
    public T? Argument<T>(int index)
    {
        return index < Arguments.Count ? Arguments[index].Deserialize<T>(ReactValue.WebOptions) : default;
    }

}
