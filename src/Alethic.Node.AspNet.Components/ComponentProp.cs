using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Web.UI;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// One prop of a <see cref="Component"/>, or one value inside one: declared in markup, it builds the component's
/// props.
/// </summary>
/// <remarks>
/// A prop is a JavaScript value. Its <see cref="Value"/> is a scalar, read as its <see cref="Type"/>: text written in
/// markup is a string unless a type says otherwise. Props nested inside it make it an object where they have names, its
/// members, and an array where they have none, its items in order. A <see cref="ComponentCallback"/> nested among them
/// is a callback. A prop that declares nothing is <c>null</c>.
///
/// A value bound with a data-binding expression keeps its own type: <c>Value='&lt;%# Order.Lines.Count %&gt;'</c> is a
/// number, and an object or a list is an object or an array, as from code. Bound values are there once the control is
/// bound.
///
/// Markup is applied again on every request, like any control's declared attributes, so a prop declared here takes no
/// view state. <see cref="Component.Props"/> is where code changes them.
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
    /// The prop's value: text written in markup, read as <see cref="Type"/>, or anything bound to it, which keeps its
    /// own type unless <see cref="Type"/> asks for another.
    /// </summary>
    /// <remarks>
    /// Text in markup reaches it as a string, through <see cref="ComponentPropValueConverter"/>: the page parser makes
    /// an <see cref="object"/> from text only through a converter. A data-binding expression assigns its value as it
    /// is.
    /// </remarks>
    [Bindable(true)]
    [TypeConverter(typeof(ComponentPropValueConverter))]
    public object? Value { get; set; }

    /// <summary>
    /// The JavaScript type <see cref="Value"/> is read as. <see cref="ComponentPropType.Auto"/> unless set: as it is.
    /// </summary>
    public ComponentPropType Type { get; set; }

    /// <summary>
    /// The value this declares.
    /// </summary>
    /// <exception cref="InvalidOperationException">It declares a value that is not its type, or contradicts
    /// itself.</exception>
    internal ComponentValue ToValue()
    {
        var nested = Controls.Cast<Control>().Where(i => i is ComponentProp or ComponentCallback).ToList();
        if (nested.Count > 0)
        {
            if (Value is not null)
                throw Invalid("declares both a Value and nested props");
            if (Type != ComponentPropType.Auto)
                throw Invalid($"has nested props, which make an object or an array, not a {Type}");

            var named = nested.Count(i => string.IsNullOrEmpty(NameOf(i)) == false);
            if (named == nested.Count)
                return Build(new ComponentObject(), this);
            if (named > 0)
                throw Invalid("has nested props with names and without: an object's members have names, an array's items none");

            var array = new ComponentArray();
            foreach (var child in nested)
                array.Add(Declared(child)!);

            return array;
        }

        // No value is null, whatever the type: so is a value not yet bound, or bound to nothing.
        if (Value is null)
            return new ComponentScalar(null);

        switch (Type)
        {
            case ComponentPropType.Auto:
                return Value is string written ? written : ComponentValue.FromObject(Value);

            case ComponentPropType.String:
                return Value as string ?? Convert.ToString(Value, CultureInfo.InvariantCulture);

            case ComponentPropType.Number:
                return Value switch
                {
                    string text => Number(text),
                    _ when IsNumeric(Value) => ComponentValue.FromObject(Value),
                    _ => throw Invalid($"is bound to a {Value.GetType().Name}, which is not a Number"),
                };

            case ComponentPropType.Boolean:
                return Value switch
                {
                    bool flag => flag,
                    string text when bool.TryParse(text.Trim(), out var parsed) => parsed,
                    _ => throw Invalid($"has a Value '{Value}' that is not a Boolean"),
                };

            case ComponentPropType.Null:
                throw Invalid("declares a Value, which a Null cannot hold");

            default:
                throw Invalid($"has no type {Type}");
        }
    }

    /// <summary>
    /// The name a nested prop or callback declares.
    /// </summary>
    /// <param name="child">The prop or callback.</param>
    static string? NameOf(Control child)
    {
        return child is ComponentProp prop ? prop.Name : ((ComponentCallback)child).Name;
    }

    /// <summary>
    /// Text as a number, written as JSON, and so as JavaScript, writes one.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="InvalidOperationException">The text is not a number.</exception>
    ComponentValue Number(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Number)
                return new ComponentScalar(document.RootElement.Clone());
        }
        catch (JsonException)
        {
        }

        throw Invalid($"has a Value '{text}' that is not a Number");
    }

    /// <summary>
    /// Whether a bound value is a number.
    /// </summary>
    /// <param name="value">The value.</param>
    static bool IsNumeric(object value)
    {
        return Convert.GetTypeCode(value) is >= TypeCode.SByte and <= TypeCode.Decimal;
    }

    /// <summary>
    /// What is thrown at a prop declared wrongly.
    /// </summary>
    /// <param name="problem">What is wrong with it.</param>
    InvalidOperationException Invalid(string problem)
    {
        return new InvalidOperationException($"The prop '{Name}' {problem}.");
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

            var name = NameOf(child);
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
