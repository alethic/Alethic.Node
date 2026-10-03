using System;
using System.Web.UI;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// One prop of a <see cref="NodeComponent"/>, or one value inside one: declared in markup, it builds the component's
/// props.
/// </summary>
/// <remarks>
/// A prop is a string (<see cref="Value"/>), any JSON value (<see cref="Json"/>), an object of the props nested inside
/// it, or, with <see cref="Array"/>, an array of the values nested inside it. A <see cref="ComponentCallback"/> nested
/// among them is a callback. <see cref="Name"/> is its key in the object it is in; an array's items have none.
///
/// Markup is applied again on every request, like any control's declared attributes, so a prop declared here takes no
/// view state. <see cref="NodeComponent.Props"/> is where code changes them.
/// </remarks>
[ParseChildren(false)]
[PersistChildren(true)]
[ControlBuilder(typeof(ComponentPropsBuilder))]
public class ComponentProp : Control
{

    /// <summary>
    /// The prop's key in the object it is in; none for an array's item.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The prop's value, as a string.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The prop's value, as JSON: a number, <c>true</c>, an object, anything.
    /// </summary>
    public string? Json { get; set; }

    /// <summary>
    /// Whether the values nested inside this are an array's items rather than an object's members.
    /// </summary>
    public bool Array { get; set; }

    /// <summary>
    /// The value this declares.
    /// </summary>
    internal ComponentValue ToValue()
    {
        if (Json is not null)
            return ComponentValue.Parse(Json);
        if (Value is not null)
            return Value;

        if (Array)
        {
            var array = new ComponentArray();
            foreach (Control child in Controls)
                if (Declared(child) is ComponentValue item)
                    array.Add(item);

            return array;
        }

        return Build(new ComponentObject(), this);
    }

    /// <summary>
    /// Adds the props a control declares to an object, by their names.
    /// </summary>
    /// <param name="target">The object.</param>
    /// <param name="parent">The control they are declared in.</param>
    /// <exception cref="InvalidOperationException">A prop has no name.</exception>
    internal static ComponentObject Build(ComponentObject target, Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (Declared(child) is not ComponentValue value)
                continue;

            var name = child is ComponentProp prop ? prop.Name : ((ComponentCallback)child).Name;
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"A prop declared in '{parent.ID ?? parent.GetType().Name}' needs a Name.");

            target[name!] = value;
        }

        return target;
    }

    /// <summary>
    /// The value a control declares, where it is a prop or a callback; <see langword="null"/> for anything else, such
    /// as the whitespace between them.
    /// </summary>
    /// <param name="child">The control.</param>
    /// <exception cref="InvalidOperationException">A callback has no command name.</exception>
    static ComponentValue? Declared(Control child)
    {
        if (child is ComponentProp prop)
            return prop.ToValue();
        if (child is ComponentCallback callback)
            return new ComponentCommand(callback.CommandName ?? throw new InvalidOperationException($"The callback '{callback.Name}' needs a CommandName."));

        return null;
    }

}
