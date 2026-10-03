using System;
using System.Text.Json.Serialization;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// What a component threw while it rendered on the server, and where.
/// </summary>
internal sealed class ComponentRenderError
{

    /// <summary>
    /// The message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The JavaScript stack, where there is one.
    /// </summary>
    public string? Stack { get; set; }

    /// <summary>
    /// Where in the component tree, where the client reports it.
    /// </summary>
    public string? ComponentStack { get; set; }

    /// <summary>
    /// The id a failed command's rejection carried, where that is what failed the component.
    /// </summary>
    public string? DotnetErrorId { get; set; }

    /// <summary>
    /// What the failed command's handler threw, where that is what failed the component.
    /// </summary>
    [JsonIgnore]
    public Exception? Exception { get; set; }

}
