using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Alethic.Node;

namespace Sample.Load;

/// <summary>
/// The pool's counters, as a collector would see them: a listener on the pool's meter, keeping each counter's total by
/// its tags.
/// </summary>
public sealed class Counters : IDisposable
{

    readonly MeterListener listener = new();
    readonly ConcurrentDictionary<string, long> totals = new();

    /// <summary>
    /// Initializes a new instance, listening from now on.
    /// </summary>
    public Counters()
    {
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == NodeEnginePool.MeterName && instrument is Counter<long>)
                l.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var name = instrument.Name;
            foreach (var tag in tags)
                name += $" {tag.Key}={tag.Value}";

            totals.AddOrUpdate(name, value, (_, total) => total + value);
        });

        listener.Start();
    }

    /// <summary>
    /// Each counter's total, by name and tags.
    /// </summary>
    public IEnumerable<KeyValuePair<string, long>> Totals => totals.OrderBy(i => i.Key);

    /// <summary>
    /// A counter's total, by its name and tags as printed.
    /// </summary>
    /// <param name="name">The name, and any tags as <c>key=value</c>.</param>
    public long this[string name] => totals.TryGetValue(name, out var total) ? total : 0;

    /// <inheritdoc />
    public void Dispose()
    {
        listener.Dispose();
    }

}
