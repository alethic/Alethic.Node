using System;
using System.Collections;
using System.Web.UI;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// Builds the <see cref="ReactProp"/>s and <see cref="ReactCallback"/>s nested in a <see cref="ReactComponent"/> or a
/// <see cref="ReactProp"/> as controls without their needing <c>runat="server"</c>, as a list's items need none.
/// </summary>
public class ReactPropsBuilder : ControlBuilder
{

    /// <summary>
    /// The control a nested tag is: a prop or a callback, whatever its prefix.
    /// </summary>
    /// <param name="tagName">The tag's name.</param>
    /// <param name="attribs">The tag's attributes.</param>
    public override Type GetChildControlType(string tagName, IDictionary attribs)
    {
        var name = tagName.Substring(tagName.IndexOf(':') + 1);
        if (string.Equals(name, nameof(ReactProp), StringComparison.OrdinalIgnoreCase))
            return typeof(ReactProp);
        if (string.Equals(name, nameof(ReactCallback), StringComparison.OrdinalIgnoreCase))
            return typeof(ReactCallback);

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
