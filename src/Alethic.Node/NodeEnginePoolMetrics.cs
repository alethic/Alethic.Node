using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Alethic.Node;

/// <summary>
/// A pool's metrics, under the meter <see cref="NodeEnginePool.MeterName"/>.
/// </summary>
/// <remarks>
/// What the pool and its engines record, as a collector reads it: the gauges are observed from the pool's statistics
/// when a collector asks, and the counters and the histogram are written as things happen. With no collector
/// listening, none of it costs anything. Each engine's figures are tagged <c>engine</c> with its id, which is stable
/// for the engine's life and never reused in the process.
/// </remarks>
sealed class NodeEnginePoolMetrics : IDisposable
{

    readonly Meter meter;
    readonly Counter<long> leases;
    readonly Counter<long> refused;
    readonly Counter<long> started;
    readonly Counter<long> retired;
    readonly Histogram<double> wait;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="statistics">The pool's statistics, read when a collector asks for the gauges.</param>
    public NodeEnginePoolMetrics(Func<NodeEnginePoolStatistics> statistics)
    {
        if (statistics is null)
            throw new ArgumentNullException(nameof(statistics));

        meter = new Meter(NodeEnginePool.MeterName);

        leases = meter.CreateCounter<long>("alethic.node.pool.leases", "{lease}", "Leases returned to the pool.");
        refused = meter.CreateCounter<long>("alethic.node.pool.acquisitions.refused", "{acquisition}", "Acquisitions that got no capacity, by reason: timeout, overload or cancelled.");
        started = meter.CreateCounter<long>("alethic.node.pool.engines.started", "{engine}", "Engines started.");
        retired = meter.CreateCounter<long>("alethic.node.pool.engines.retired", "{engine}", "Engines retired.");
        wait = meter.CreateHistogram<double>("alethic.node.pool.acquisition.wait", "ms", "How long an acquisition waited in line before it was given capacity.");

        meter.CreateObservableGauge("alethic.node.pool.engines", () => (long)statistics().Engines.Count, "{engine}", "Engines running.");
        meter.CreateObservableGauge("alethic.node.pool.queued", () => (long)statistics().Queued, "{acquisition}", "Acquisitions waiting for capacity.");
        meter.CreateObservableGauge("alethic.node.pool.overloaded", () => statistics().Overloaded ? 1L : 0L, "{overloaded}", "Whether the pool counts itself overloaded.");

        meter.CreateObservableGauge("alethic.node.engine.leases", () => PerEngine(statistics(), i => (long)i.InFlight), "{lease}", "Leases held against the engine.");
        meter.CreateObservableGauge("alethic.node.engine.limit", () => PerEngine(statistics(), i => (long)i.Limit), "{lease}", "The most leases the engine may hold at once.");
        meter.CreateObservableGauge("alethic.node.engine.loop_delay", () => PerEngine(statistics(), i => i.LoopDelay), "ms", "How long work posted to the engine waited before its thread ran it, over the last window.");
        meter.CreateObservableGauge("alethic.node.engine.heap.used", () => PerEngine(statistics(), i => i.HeapUsed), "By", "The engine's heap in use.");
        meter.CreateObservableGauge("alethic.node.engine.heap.total", () => PerEngine(statistics(), i => i.HeapTotal), "By", "The engine's heap committed.");
        meter.CreateObservableGauge("alethic.node.engine.heap.limit", () => PerEngine(statistics(), i => i.HeapLimit), "By", "The most the engine's heap may grow to.");
        meter.CreateObservableGauge("alethic.node.engine.external_memory", () => PerEngine(statistics(), i => i.ExternalMemory), "By", "The memory outside the engine's heap that its objects hold.");
    }

    /// <summary>
    /// One measurement per engine, tagged with the engine's id.
    /// </summary>
    /// <typeparam name="T">The measurement's type.</typeparam>
    /// <param name="statistics">The pool's statistics.</param>
    /// <param name="measure">The figure to take from each engine.</param>
    static IEnumerable<Measurement<T>> PerEngine<T>(NodeEnginePoolStatistics statistics, Func<NodeEngineStatistics, T> measure)
        where T : struct
    {
        foreach (var engine in statistics.Engines)
            yield return new Measurement<T>(measure(engine), new KeyValuePair<string, object?>("engine", engine.Id));
    }

    /// <summary>
    /// A lease was returned.
    /// </summary>
    public void LeaseReturned()
    {
        leases.Add(1);
    }

    /// <summary>
    /// An acquisition got no capacity.
    /// </summary>
    /// <param name="reason">Why: timeout, overload or cancelled.</param>
    public void AcquisitionRefused(string reason)
    {
        refused.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// An acquisition that waited in line was given capacity.
    /// </summary>
    /// <param name="waited">How long it waited, in milliseconds.</param>
    public void AcquisitionServed(double waited)
    {
        wait.Record(waited);
    }

    /// <summary>
    /// An engine started.
    /// </summary>
    public void EngineStarted()
    {
        started.Add(1);
    }

    /// <summary>
    /// An engine was retired.
    /// </summary>
    public void EngineRetired()
    {
        retired.Add(1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        meter.Dispose();
    }

}
