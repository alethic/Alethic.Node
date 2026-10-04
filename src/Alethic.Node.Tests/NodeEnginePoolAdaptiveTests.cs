using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// A pool that learns its limits from the work it is given. Each test adapts by hand, at the moments it chooses, rather
/// than on the pool's own interval.
/// </summary>
[TestClass]
public class NodeEnginePoolAdaptiveTests
{

    /// <summary>
    /// Work that keeps the engine's thread busy, and work that only waits.
    /// </summary>
    static readonly NodeModuleSource Work = TestModules.FromText("work.cjs", """
        module.exports.busy = ms => {
            const end = Date.now() + ms;
            while (Date.now() < end) { }
            return true;
        };
        module.exports.wait = ms => new Promise(resolve => setTimeout(() => resolve(true), ms));
        """);

    /// <summary>
    /// A pool on one engine that adapts only when told to.
    /// </summary>
    /// <param name="max">The most leases the engine may hold, and where its limit starts.</param>
    static NodeEnginePool Pool(int max)
    {
        return new NodeEnginePool(new NodeEnginePoolOptions()
        {
            Mode = NodeEnginePoolMode.Adaptive,
            MaxConcurrencyPerEngine = max,
            TargetEventLoopDelay = TimeSpan.FromMilliseconds(40),
            AdaptInterval = Timeout.InfiniteTimeSpan,
        }, NullLoggerFactory.Instance, new NoServices());
    }

    /// <summary>
    /// Runs calls to an export side by side, for a while, as many at once as the pool lets through.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="export">The export: busy or wait.</param>
    /// <param name="ms">What each call is given.</param>
    /// <param name="callers">How many callers there are.</param>
    /// <param name="duration">How long they keep calling.</param>
    static Task LoadAsync(NodeEnginePool pool, string export, int ms, int callers, TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;

        return Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            while (DateTime.UtcNow < until)
                await pool.RunAsync(Work, async exports =>
                {
                    var result = exports.CallMethod(export, ms);
                    return result.IsPromise() ? (bool)await ((JSPromise)result).AsTask() : (bool)result;
                });
        })));
    }

    /// <summary>
    /// The engine's limit as it stands.
    /// </summary>
    /// <param name="pool">The pool.</param>
    static int Limit(NodeEnginePool pool)
    {
        return pool.GetStatistics().Engines.Single().Limit;
    }

    /// <summary>
    /// Work that keeps the engine's thread busy past the target delay brings the engine's limit down; work that only
    /// waits, with the limit what holds it back, takes it up again.
    /// </summary>
    [TestMethod]
    public async Task The_limit_falls_under_busy_work_and_rises_under_waiting_work()
    {
        await using var pool = Pool(max: 8);

        // The first reading starts the measuring.
        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await pool.AdaptAsync();
        Assert.AreEqual(8, Limit(pool));

        await LoadAsync(pool, "busy", 100, callers: 8, TimeSpan.FromMilliseconds(600));
        await pool.AdaptAsync();

        var fallen = Limit(pool);
        Assert.IsTrue(fallen < 8, $"The limit stayed at {fallen} with the event loop {pool.GetStatistics().Engines.Single().LoopDelay} ms late.");
        Assert.IsTrue(pool.GetStatistics().Engines.Single().LoopDelay > 40);

        await LoadAsync(pool, "wait", 50, callers: 16, TimeSpan.FromMilliseconds(600));
        await pool.AdaptAsync();

        var risen = Limit(pool);
        Assert.IsTrue(risen > fallen, $"The limit stayed at {risen} with the event loop {pool.GetStatistics().Engines.Single().LoopDelay} ms late.");
    }

    /// <summary>
    /// With the limit lowered, the engine holds no more leases than it allows; the rest wait their turn.
    /// </summary>
    [TestMethod]
    public async Task A_lowered_limit_holds_back_what_is_over_it()
    {
        await using var pool = Pool(max: 4);

        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await pool.AdaptAsync();

        await LoadAsync(pool, "busy", 100, callers: 4, TimeSpan.FromMilliseconds(500));
        await pool.AdaptAsync();
        var limit = Limit(pool);
        Assert.IsTrue(limit < 4);

        var leases = await Task.WhenAll(Enumerable.Range(0, limit).Select(_ => pool.AcquireAsync()));
        var over = pool.AcquireAsync();

        Assert.IsFalse(over.IsCompleted);
        Assert.AreEqual(1, pool.GetStatistics().Queued);

        await leases[0].DisposeAsync();
        await using var given = await over;

        foreach (var lease in leases.Skip(1))
            await lease.DisposeAsync();
    }

    /// <summary>
    /// The delay is read from the leases themselves: one posted behind busy work waits for it, and that wait is the
    /// window's delay; a window in which nothing was posted reads nothing.
    /// </summary>
    [TestMethod]
    public async Task The_delay_is_what_the_leases_waited()
    {
        await using var pool = Pool(max: 2);

        // The engine started, so the two below queue on it rather than on its starting.
        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await pool.AdaptAsync();

        var busy = pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 300)));
        await Task.Delay(50);
        var behind = pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await Task.WhenAll(busy, behind);

        await pool.AdaptAsync();
        // Posted some time into the busy work, however long the posting took on the machine at hand, and waited the
        // rest of it: well over any wait an idle engine would show, and no more than the busy work.
        var delay = pool.GetStatistics().Engines.Single().LoopDelay;
        Assert.IsTrue(delay is >= 50 and <= 400, $"The lease behind the busy one waited {delay} ms.");

        await Task.Delay(100);
        await pool.AdaptAsync();
        Assert.AreEqual(0, pool.GetStatistics().Engines.Single().LoopDelay);
    }

    /// <summary>
    /// A fixed pool keeps its limit, whatever its engine's delay, and adapts nothing when asked to.
    /// </summary>
    [TestMethod]
    public async Task A_fixed_pool_keeps_its_limit()
    {
        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions() { MaxConcurrencyPerEngine = 3, TargetEventLoopDelay = TimeSpan.FromMilliseconds(1) }, NullLoggerFactory.Instance, new NoServices());

        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        var busy = pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 100)));
        await Task.Delay(20);
        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await busy;

        await pool.AdaptAsync();
        Assert.AreEqual(3, Limit(pool));
        Assert.AreEqual(0, pool.GetStatistics().Engines.Single().LoopDelay);
    }

    /// <summary>
    /// Work still waiting when the window closes counts for what it has waited so far, and only the rest of its wait
    /// counts in the next window: an engine stuck for a whole window shows up in it.
    /// </summary>
    [TestMethod]
    public async Task Work_still_waiting_counts_in_the_window_it_waited_in()
    {
        await using var pool = Pool(max: 2);

        await pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await pool.AdaptAsync();

        var busy = pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 500)));
        await Task.Delay(50);
        var behind = pool.RunAsync(Work, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));

        // The window closes with the busy work under way and the other still waiting behind it.
        await Task.Delay(200);
        await pool.AdaptAsync();
        var soFar = pool.GetStatistics().Engines.Single().LoopDelay;
        Assert.IsTrue(soFar is >= 100 and <= 400, $"The work still waiting counted {soFar} ms.");

        await Task.WhenAll(busy, behind);
        await pool.AdaptAsync();
        var rest = pool.GetStatistics().Engines.Single().LoopDelay;
        Assert.IsTrue(rest is >= 100 and <= 450, $"The rest of the wait counted {rest} ms.");
    }

    /// <summary>
    /// An adaptive pool prepares its fewest engines, not its most: the rest are its to learn.
    /// </summary>
    [TestMethod]
    public async Task An_adaptive_pool_prepares_its_fewest_engines()
    {
        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions()
        {
            Mode = NodeEnginePoolMode.Adaptive,
            MinEngineCount = 1,
            EngineCount = 3,
            AdaptInterval = Timeout.InfiniteTimeSpan,
        }, NullLoggerFactory.Instance, new NoServices());

        await pool.PrepareAsync();
        Assert.AreEqual(1, pool.GetStatistics().Engines.Count);
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
