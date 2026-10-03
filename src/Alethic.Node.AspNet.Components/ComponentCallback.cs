using System.Web.UI;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// A callback prop of a <see cref="NodeComponent"/>, declared in markup among its <see cref="ComponentProp"/>s: the
/// function at <see cref="Name"/> raises the control's command named <see cref="CommandName"/>.
/// </summary>
public class ComponentCallback : Control
{

    /// <summary>
    /// The callback's key in the object it is in: <c>onSelect</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The name it raises: what the page dispatches on in its handler of <see cref="NodeComponent.Command"/>.
    /// </summary>
    public string? CommandName { get; set; }

}
