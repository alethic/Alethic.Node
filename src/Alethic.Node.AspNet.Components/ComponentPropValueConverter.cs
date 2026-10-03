using System;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// Lets text written in markup be a <see cref="ComponentProp.Value"/>, which is an <see cref="object"/> so that a bound
/// value keeps its type.
/// </summary>
/// <remarks>
/// The page parser makes a property's value from text through its converter, and the page compiler writes that value as
/// code through it too: for an <see cref="object"/> it can do neither by itself. Text stays text, and is written as
/// <c>System.Convert.ToString("…")</c>, which is the text as an <see cref="object"/>.
/// </remarks>
internal sealed class ComponentPropValueConverter : TypeConverter
{

    /// <summary>
    /// Text converts.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="sourceType">The type converted from.</param>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <summary>
    /// Text is itself.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="culture">The culture.</param>
    /// <param name="value">The text.</param>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return value is string ? value : base.ConvertFrom(context, culture, value);
    }

    /// <summary>
    /// Text can be written as code.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="destinationType">The type converted to.</param>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);
    }

    /// <summary>
    /// Text, as the code that makes it: <c>System.Convert.ToString("…")</c>.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="culture">The culture.</param>
    /// <param name="value">The text.</param>
    /// <param name="destinationType">The type converted to.</param>
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(InstanceDescriptor) && value is string text)
            return new InstanceDescriptor(typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(string)]), new object[] { text });

        return base.ConvertTo(context, culture, value, destinationType);
    }

}
