using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// An array in a <see cref="NodeComponent"/>'s props.
/// </summary>
public sealed class ComponentArray : ComponentValue, IEnumerable<ComponentValue>
{

    readonly List<ComponentValue> items = [];

    /// <inheritdoc />
    public override ComponentValue? this[int index]
    {
        get => items[index];
        set => items[index] = value ?? new ComponentScalar(null);
    }

    /// <summary>
    /// The number of items.
    /// </summary>
    public int Count => items.Count;

    /// <summary>
    /// Adds an item, also for collection initializers: <c>new ComponentArray { "sku", "name" }</c>.
    /// </summary>
    /// <param name="value">The item.</param>
    public void Add(ComponentValue? value) => items.Add(value ?? new ComponentScalar(null));

    /// <summary>
    /// Takes the item at a position away.
    /// </summary>
    /// <param name="index">The position.</param>
    public void RemoveAt(int index) => items.RemoveAt(index);

    /// <inheritdoc />
    public IEnumerator<ComponentValue> GetEnumerator() => items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    internal override void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (var item in items)
            item.WriteTo(writer);

        writer.WriteEndArray();
    }

}
