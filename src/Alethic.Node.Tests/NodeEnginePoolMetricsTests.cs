using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// What a pool publishes under its meter.
/// </summary>
[TestClass]
public class NodeEnginePoolMetricsTests
{

    /// <summary>
    /// Everything measured under the meter, by instrument: each value with its tags.
    /// </summary>
    sealed class Measured
    {

        /// <summary>
        /// The measurements, by instrument name.
        /// </summary>
        public Dictionary<string, List<(double Value, Dictionary<string, object?> Tags)>> Values { get; } = new();

        /// <summary>
        /// Records one.
        /// </summary>
        /// <param name="instrument">The instrument.</param>
        /// <param name="value">The value.</param>
        /// <param name="tags">Its tags.</param>
        public void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var tag in tags)
                dictionary[tag.Key] = tag.Value;

            lock (Values)
            {
                if (Values.TryGetValue(instrument.Name, out var list) == false)
                    Values[instrument.Name] = list = new();

                list.Add((value, dictionary));
            }
        }

        /// <summary>
        /// The sum of an instrument's values, those with a tag of the given value where one is named.
        /// </summary>
        /// <param name="instrument">The instrument.</param>
        /// <param name="tag">The tag, where one is named.</param>
        /// <param name="tagValue">The tag's value.</param>
        public double Sum(string instrument, string? tag = null, object? tagValue = null)
        {
            lock (Values)
                return Values.TryGetValue(instrument, out var list)
                    ? list.Where(i => tag is null || (i.Tags.TryGetValue(tag, out var v) && Equals(v, tagValue))).Sum(i => i.Value)
                    : 0;
        }

        /// <summary>
        /// The latest value of an instrument with a tag of the given value.
        /// </summary>
        /// <param name="instrument">The instrument.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="tagValue">The tag's value.</param>
        public double? Latest(string instrument, string tag, object? tagValue)
        {
            lock (Values)
                return Values.TryGetValue(instrument, out var list)
                    ? list.Where(i => i.Tags.TryGetValue(tag, out var v) && Equals(v, tagValue)).Select(i => (double?)i.Value).LastOrDefault()
                    : null;
        }

    }

    /// <summary>
    /// Listens to every pool's meter.
    /// </summary>
    /// <param name="measured">Where the measurements go.</param>
    static MeterListener Listen(Measured measured)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == NodeEnginePool.MeterName)
                l.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measured.Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measured.Record(instrument, value, tags));
        listener.Start();
        return listener;
    }

    /// <summary>
    /// The counters count what happened, the histogram what was waited, and the gauges show the pool and each of its
    /// engines, by id, as the statistics do.
    /// </summary>
    [TestMethod]
    public async Task The_pool_publishes_its_figures()
    {
        var measured = new Measured();
        using var listener = Listen(measured);

        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions()
        {
            MaxConcurrencyPerEngine = 1,
            AcquireTimeout = TimeSpan.FromMilliseconds(200),
        }, NullLoggerFactory.Instance, new NoServices());

        // One lease returned at once; one refused for the timeout, behind a held one; then one that waits in line
        // behind the held one and is served when it is returned.
        await pool.RunAsync(() => Task.FromResult(true));
        var held = await pool.AcquireAsync();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => pool.AcquireAsync());
        var waiting = pool.AcquireAsync();
        await Task.Delay(20);
        await held.DisposeAsync();
        await using var served = await waiting;

        listener.RecordObservableInstruments();
        var engine = pool.GetStatistics().Engines.Single();

        Assert.AreEqual(1, measured.Sum("alethic.node.pool.engines.started"));
        Assert.AreEqual(2, measured.Sum("alethic.node.pool.leases"));
        Assert.AreEqual(1, measured.Sum("alethic.node.pool.acquisitions.refused", "reason", "timeout"));
        Assert.AreEqual(0, measured.Sum("alethic.node.pool.acquisitions.refused", "reason", "overload"));
        Assert.AreEqual(1, measured.Values["alethic.node.pool.acquisition.wait"].Count);
        Assert.IsTrue(measured.Values["alethic.node.pool.acquisition.wait"].Single().Value > 0);

        Assert.AreEqual(1, measured.Sum("alethic.node.pool.engines"));
        Assert.AreEqual(0, measured.Sum("alethic.node.pool.queued"));
        Assert.AreEqual(0, measured.Sum("alethic.node.pool.overloaded"));

        Assert.AreEqual(1, measured.Latest("alethic.node.engine.leases", "engine", engine.Id));
        Assert.AreEqual(1, measured.Latest("alethic.node.engine.limit", "engine", engine.Id));
        Assert.AreEqual(engine.HeapUsed, measured.Latest("alethic.node.engine.heap.used", "engine", engine.Id));
        Assert.AreEqual(engine.HeapLimit, measured.Latest("alethic.node.engine.heap.limit", "engine", engine.Id));
        Assert.IsTrue(measured.Latest("alethic.node.engine.heap.used", "engine", engine.Id) > 0);
    }

    /// <summary>
    /// Supplies nothing.
    /// </summary>
    sealed class NoServices : IServiceProvider
    {

        /// <summary>
        /// Supplies nothing.
        /// </summary>
        /// <param name="serviceType">What is asked for.</param>
        public object? GetService(Type serviceType) => null;

    }

}
