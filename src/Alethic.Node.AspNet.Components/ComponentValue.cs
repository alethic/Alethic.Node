using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// A value in a <see cref="NodeComponent"/>'s props: a <see cref="ComponentScalar"/>, a <see cref="ComponentObject"/>,
/// a <see cref="ComponentArray"/>, or a <see cref="ComponentCommand"/>, which the component receives as a function.
/// </summary>
/// <remarks>
/// Props are what JSON can carry, plus callbacks. A callback's only state is the name of the command it raises. The
/// tree is written natively for each place it goes: as JavaScript values on the server render's Node engine, a callback
/// a real function calling back into .NET; as a JavaScript literal in the browser, a callback a function raising the
/// control's command there; and as JSON in view state, a callback its name in an object of its own.
///
/// Strings, numbers and booleans convert implicitly, so <c>Props["title"] = "Pipettes"</c> reads as it should.
/// </remarks>
public abstract class ComponentValue
{

    /// <summary>
    /// How <see cref="FromObject"/> writes an object: camel-cased, as JavaScript names its properties.
    /// </summary>
    internal static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The value at a key, where this is an object; <see langword="null"/> where it has no such key.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <exception cref="InvalidOperationException">This is not an object.</exception>
    public virtual ComponentValue? this[string name]
    {
        get => throw new InvalidOperationException($"A {GetType().Name} has no keys.");
        set => throw new InvalidOperationException($"A {GetType().Name} has no keys.");
    }

    /// <summary>
    /// The item at a position, where this is an array.
    /// </summary>
    /// <param name="index">The position.</param>
    /// <exception cref="InvalidOperationException">This is not an array.</exception>
    public virtual ComponentValue? this[int index]
    {
        get => throw new InvalidOperationException($"A {GetType().Name} has no items.");
        set => throw new InvalidOperationException($"A {GetType().Name} has no items.");
    }

    /// <summary>
    /// Writes this as JSON, each callback as its command's view-state object.
    /// </summary>
    /// <param name="writer">The writer.</param>
    internal abstract void WriteTo(Utf8JsonWriter writer);

    /// <summary>
    /// This as JSON, each callback as its command's view-state object.
    /// </summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteTo(writer);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads props written by <see cref="ToJson"/>, or any JSON.
    /// </summary>
    /// <param name="json">The JSON.</param>
    public static ComponentValue Parse(string json)
    {
        if (json is null)
            throw new ArgumentNullException(nameof(json));

        using var document = JsonDocument.Parse(json);
        return From(document.RootElement);
    }

    /// <summary>
    /// Any object, as props: whatever <see cref="JsonSerializer"/> makes of it, camel-cased as JavaScript's properties
    /// are. A <see cref="ComponentValue"/> is itself.
    /// </summary>
    /// <param name="value">The object.</param>
    public static ComponentValue FromObject(object? value)
    {
        if (value is ComponentValue already)
            return already;

        return From(JsonSerializer.SerializeToElement(value, WebOptions));
    }

    /// <summary>
    /// A JSON element, as props, with each command's view-state object read back as a callback.
    /// </summary>
    /// <param name="element">The element.</param>
    internal static ComponentValue From(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (ComponentCommand.Read(element) is ComponentCommand command)
                    return command;

                var obj = new ComponentObject();
                foreach (var property in element.EnumerateObject())
                    obj[property.Name] = From(property.Value);
                return obj;

            case JsonValueKind.Array:
                var array = new ComponentArray();
                foreach (var item in element.EnumerateArray())
                    array.Add(From(item));
                return array;

            default:
                return new ComponentScalar(element.Clone());
        }
    }

    /// <summary>
    /// A string, as props.
    /// </summary>
    /// <param name="value">The string, or <see langword="null"/> for <c>null</c>.</param>
    public static implicit operator ComponentValue(string? value) => new ComponentScalar(value);

    /// <summary>
    /// A boolean, as props.
    /// </summary>
    /// <param name="value">The boolean.</param>
    public static implicit operator ComponentValue(bool value) => new ComponentScalar(value);

    /// <summary>
    /// A number, as props.
    /// </summary>
    /// <param name="value">The number.</param>
    public static implicit operator ComponentValue(int value) => new ComponentScalar(value);

    /// <summary>
    /// A number, as props.
    /// </summary>
    /// <param name="value">The number.</param>
    public static implicit operator ComponentValue(long value) => new ComponentScalar(value);

    /// <summary>
    /// A number, as props.
    /// </summary>
    /// <param name="value">The number.</param>
    public static implicit operator ComponentValue(double value) => new ComponentScalar(value);

    /// <summary>
    /// A number, as props.
    /// </summary>
    /// <param name="value">The number.</param>
    public static implicit operator ComponentValue(decimal value) => new ComponentScalar(value);

}
