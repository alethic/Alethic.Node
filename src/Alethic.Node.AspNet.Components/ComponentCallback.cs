using System.Web.UI;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// A callback prop of a <see cref="ReactComponent"/>, declared in markup among its <see cref="ReactProp"/>s: the
/// function at <see cref="Name"/> raises the control's command named <see cref="CommandName"/>.
/// </summary>
public class ReactCallback : Control
{

    /// <summary>
    /// The callback's key in the object it is in: <c>onSelect</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The name it raises: what the page dispatches on in its handler of <see cref="ReactComponent.Command"/>.
    /// </summary>
    public string? CommandName { get; set; }

}
