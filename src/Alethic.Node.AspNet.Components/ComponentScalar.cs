using System;
using System.Text.Json;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// A string, number, boolean or <c>null</c> in a <see cref="Component"/>'s props.
/// </summary>
public sealed class ComponentScalar : ComponentValue
{

    readonly object? value;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="value">A string, a number, a boolean, a <see cref="JsonElement"/> holding one, or
    /// <see langword="null"/>.</param>
    public ComponentScalar(object? value)
    {
        this.value = value;
    }

    /// <summary>
    /// The value, read as a type of the caller's.
    /// </summary>
    /// <typeparam name="T">The type.</typeparam>
    public T? Get<T>()
    {
        if (value is JsonElement element)
            return element.Deserialize<T>(WebOptions);
        if (value is null)
            return default;

        return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    /// <inheritdoc />
    internal override void WriteTo(Utf8JsonWriter writer)
    {
        if (value is JsonElement element)
            element.WriteTo(writer);
        else
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ToJson();
    }

}
