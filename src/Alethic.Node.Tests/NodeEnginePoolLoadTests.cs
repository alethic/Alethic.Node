using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// An adaptive pool under load, left to adapt on its own clock with its default target and window: what it does with
/// work shaped like a site's.
/// </summary>
[TestClass]
public class NodeEnginePoolLoadTests
{

    /// <summary>
    /// A render that computes, and one that waits on something else, as a fetch does.
    /// </summary>
    static readonly NodeModuleSource Renders = TestModules.FromText("renders.cjs", """
        module.exports.compute = ms => {
            const end = Date.now() + ms;
            while (Date.now() < end) { }
            return true;
        };
        module.exports.fetch = ms => new Promise(resolve => setTimeout(() => resolve(true), ms));
        """);

    /// <summary>
    /// Callers calling for a while, as many at once as the pool lets through.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="export">The render: compute or fetch.</param>
    /// <param name="ms">What each render is given.</param>
    /// <param name="callers">How many callers there are.</param>
    /// <param name="duration">How long they keep calling.</param>
    /// <returns>How many renders completed.</returns>
    static async Task<int> LoadAsync(NodeEnginePool pool, string export, int ms, int callers, TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        var completed = 0;

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            while (DateTime.UtcNow < until)
            {
                await pool.RunAsync(Renders, async exports =>
                {
                    var result = exports.CallMethod(export, ms);
                    return result.IsPromise() ? (bool)await ((JSPromise)result).AsTask() : (bool)result;
                });

                Interlocked.Increment(ref completed);
            }
        })));

        return completed;
    }

    /// <summary>
    /// Renders that compute bring an engine's limit down from where it starts, since each waits on the ones before it
    /// for far longer than the target; renders that wait, with the limit what holds them back, take it up again. The
    /// pool adapts by itself, at its default target and interval.
    /// </summary>
    [TestMethod]
    public async Task Under_load_the_limit_falls_for_computing_renders_and_rises_for_waiting_ones()
    {
        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions()
        {
            Mode = NodeEnginePoolMode.Adaptive,
            MaxConcurrencyPerEngine = 32,
        }, NullLoggerFactory.Instance, new NoServices());

        await pool.PrepareAsync();
        Assert.AreEqual(32, pool.GetStatistics().Engines.Single().Limit);

        // Three windows and more of 32 callers computing 30 ms each: a queue of some 900 ms behind each, so the limit
        // halves each window.
        await LoadAsync(pool, "compute", 30, callers: 32, TimeSpan.FromSeconds(3.5));
        var fallen = pool.GetStatistics().Engines.Single();
        Assert.IsTrue(fallen.Limit <= 8, $"The limit only fell to {fallen.Limit}, with the event loop {fallen.LoopDelay:0} ms late.");
        Assert.IsTrue(fallen.LoopDelay > 40, $"The event loop was only {fallen.LoopDelay:0} ms late.");

        // Then 32 callers waiting 30 ms each: the loop keeps up, and the limit is what holds the callers back, so it
        // rises every window. A second of it first, at the fallen limit, for the throughput there.
        var before = await LoadAsync(pool, "fetch", 30, callers: 32, TimeSpan.FromSeconds(1));
        var after = await LoadAsync(pool, "fetch", 30, callers: 32, TimeSpan.FromSeconds(4.5));
        var risen = pool.GetStatistics().Engines.Single();
        Assert.IsTrue(risen.Limit >= fallen.Limit + 2, $"The limit only rose from {fallen.Limit} to {risen.Limit}, with the event loop {risen.LoopDelay:0} ms late.");
        Assert.IsTrue(risen.LoopDelay <= 40, $"The event loop was {risen.LoopDelay:0} ms late under waiting work.");

        // And the rise bought throughput: renders a second over the rest of the phase, half of which ran under lower
        // limits still, against the first second at the fallen limit.
        Assert.IsTrue(after / 4.5 > before * 1.5, $"{before} renders in the first second, then {after} in 4.5 s, at a limit that rose from {fallen.Limit} to {risen.Limit}.");
    }

    /// <summary>
    /// Computing renders, with acquisitions waiting and a core to spare, get a second engine within a few windows, and
    /// keep it, since it does as much again. The pool adapts by itself.
    /// </summary>
    [TestMethod]
    public async Task Under_load_a_second_engine_is_tried_and_kept()
    {
        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions()
        {
            Mode = NodeEnginePoolMode.Adaptive,
            MinEngineCount = 1,
            EngineCount = 2,
            MinConcurrencyPerEngine = 2,
            MaxConcurrencyPerEngine = 2,
        }, NullLoggerFactory.Instance, new NoServices());

        await pool.PrepareAsync();
        Assert.AreEqual(1, pool.GetStatistics().Engines.Count);

        // A window to see the line, one to start the engine, two of trial, and one to spare.
        await LoadAsync(pool, "compute", 20, callers: 8, TimeSpan.FromSeconds(5.5));
        Assert.AreEqual(2, pool.GetStatistics().Engines.Count, "The second engine was not tried, or not kept.");
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
