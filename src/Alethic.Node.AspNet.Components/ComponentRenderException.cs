using System;
using System.Web;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// A component of a <see cref="NodeComponent"/> failed while it rendered on the server: it threw, or rejected a promise
/// of one of its callbacks without catching it. Thrown from the control's own render, as any control's failure to
/// render is.
/// </summary>
public class ComponentRenderException : HttpException
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="component">The component.</param>
    /// <param name="controlId">The control's id.</param>
    /// <param name="message">What the component threw.</param>
    /// <param name="scriptStack">The JavaScript stack, where there is one.</param>
    /// <param name="componentStack">Where in the component tree, where the client reports it.</param>
    /// <param name="innerException">What a command's handler threw, where that is what failed the component.</param>
    public ComponentRenderException(string component, string? controlId, string message, string? scriptStack, string? componentStack, Exception? innerException)
        : base($"The component {component} of '{controlId}' failed to render on the server: {message}" +
            (string.IsNullOrEmpty(componentStack) ? "" : Environment.NewLine + "Component stack:" + componentStack) +
            (string.IsNullOrEmpty(scriptStack) || innerException is not null ? "" : Environment.NewLine + "Script stack:" + Environment.NewLine + scriptStack),
            innerException)
    {
        Component = component;
        ScriptStack = scriptStack;
        ComponentStack = componentStack;
    }

    /// <summary>
    /// The component that failed.
    /// </summary>
    public string Component { get; }

    /// <summary>
    /// The JavaScript stack, where there is one.
    /// </summary>
    public string? ScriptStack { get; }

    /// <summary>
    /// Where in the component tree it failed, where the client reports it.
    /// </summary>
    public string? ComponentStack { get; }

}
