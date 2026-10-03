using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// An object in a <see cref="Component"/>'s props: values by key, in the order they were set.
/// </summary>
public sealed class ComponentObject : ComponentValue, IEnumerable<KeyValuePair<string, ComponentValue>>
{

    readonly List<string> order = [];
    readonly Dictionary<string, ComponentValue> values = [];

    /// <summary>
    /// The value at a key; <see langword="null"/> where there is none. Setting <see langword="null"/> sets the value
    /// <c>null</c>; <see cref="Remove"/> takes the key away.
    /// </summary>
    /// <param name="name">The key.</param>
    public override ComponentValue? this[string name]
    {
        get => values.TryGetValue(name, out var value) ? value : null;
        set
        {
            if (values.ContainsKey(name) == false)
                order.Add(name);

            values[name] = value ?? new ComponentScalar(null);
        }
    }

    /// <summary>
    /// The number of keys.
    /// </summary>
    public int Count => order.Count;

    /// <summary>
    /// Whether there is a value at a key.
    /// </summary>
    /// <param name="name">The key.</param>
    public bool ContainsKey(string name) => values.ContainsKey(name);

    /// <summary>
    /// Sets a value, for collection initializers: <c>new ComponentObject { { "title", "Pipettes" } }</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, ComponentValue? value) => this[name] = value;

    /// <summary>
    /// Takes a key away, so the component's default applies.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>Whether there was a value at the key.</returns>
    public bool Remove(string name)
    {
        order.Remove(name);
        return values.Remove(name);
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, ComponentValue>> GetEnumerator()
    {
        foreach (var name in order)
            yield return new KeyValuePair<string, ComponentValue>(name, values[name]);
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    internal override void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var pair in this)
        {
            writer.WritePropertyName(pair.Key);
            pair.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

}
