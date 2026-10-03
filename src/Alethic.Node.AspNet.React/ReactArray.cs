using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// An array in a <see cref="ReactComponent"/>'s props.
/// </summary>
public sealed class ReactArray : ReactValue, IEnumerable<ReactValue>
{

    readonly List<ReactValue> items = [];

    /// <inheritdoc />
    public override ReactValue? this[int index]
    {
        get => items[index];
        set => items[index] = value ?? new ReactScalar(null);
    }

    /// <summary>
    /// The number of items.
    /// </summary>
    public int Count => items.Count;

    /// <summary>
    /// Adds an item, also for collection initializers: <c>new ReactArray { "sku", "name" }</c>.
    /// </summary>
    /// <param name="value">The item.</param>
    public void Add(ReactValue? value) => items.Add(value ?? new ReactScalar(null));

    /// <summary>
    /// Takes the item at a position away.
    /// </summary>
    /// <param name="index">The position.</param>
    public void RemoveAt(int index) => items.RemoveAt(index);

    /// <inheritdoc />
    public IEnumerator<ReactValue> GetEnumerator() => items.GetEnumerator();

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
