namespace Alethic.Node.AspNet.Components;

/// <summary>
/// What became of one component in a server render: its HTML, or why there is none.
/// </summary>
internal sealed class ComponentRendered
{

    /// <summary>
    /// The component's HTML, where it rendered.
    /// </summary>
    public string? Html { get; set; }

    /// <summary>
    /// Why it did not, where it did not.
    /// </summary>
    public ComponentRenderError? Error { get; set; }

}
