using System;
using System.Collections;
using System.Web.UI;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// Builds the <see cref="ComponentProp"/>s and <see cref="ComponentCallback"/>s nested in a <see cref="NodeComponent"/>
/// or a <see cref="ComponentProp"/> as controls without their needing <c>runat="server"</c>, as a list's items need
/// none.
/// </summary>
public class ComponentPropsBuilder : ControlBuilder
{

    /// <summary>
    /// The control a nested tag is: a prop or a callback, whatever its prefix.
    /// </summary>
    /// <param name="tagName">The tag's name.</param>
    /// <param name="attribs">The tag's attributes.</param>
    public override Type GetChildControlType(string tagName, IDictionary attribs)
    {
        var name = tagName.Substring(tagName.IndexOf(':') + 1);
        if (string.Equals(name, nameof(ComponentProp), StringComparison.OrdinalIgnoreCase))
            return typeof(ComponentProp);
        if (string.Equals(name, nameof(ComponentCallback), StringComparison.OrdinalIgnoreCase))
            return typeof(ComponentCallback);

        return base.GetChildControlType(tagName, attribs);
    }

    /// <summary>
    /// Leaves out the whitespace between the nested tags, which is not the component's.
    /// </summary>
    public override bool AllowWhitespaceLiterals()
    {
        return false;
    }

}
