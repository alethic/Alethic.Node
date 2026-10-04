using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// An adaptive pool learning how many engines to run. Each test adapts by hand, at the moments it chooses.
/// </summary>
[TestClass]
public class NodeEnginePoolGrowthTests
{

    /// <summary>
    /// Work that keeps an engine's thread busy.
    /// </summary>
    static readonly NodeModuleSource Busy = TestModules.FromText("busy.cjs", """
        module.exports.busy = ms => {
            const end = Date.now() + ms;
            while (Date.now() < end) { }
            return true;
        };
        """);

    /// <summary>
    /// A pool of one to two engines, two leases each, that adapts only when told to, and whose engine limits stay put:
    /// the delay target is out of reach, so only the number of engines is learned.
    /// </summary>
    /// <param name="idle">How long an engine holds nothing before it is retired.</param>
    static NodeEnginePool Pool(TimeSpan idle)
    {
        return new NodeEnginePool(new NodeEnginePoolOptions()
        {
            Mode = NodeEnginePoolMode.Adaptive,
            MinEngineCount = 1,
            EngineCount = 2,
            MinConcurrencyPerEngine = 2,
            MaxConcurrencyPerEngine = 2,
            TargetEventLoopDelay = TimeSpan.FromSeconds(10),
            EngineIdleTimeout = idle,
            AdaptInterval = Timeout.InfiniteTimeSpan,
        }, NullLoggerFactory.Instance, new NoServices());
    }

    /// <summary>
    /// Callers calling for as long as they are let, until stopped.
    /// </summary>
    /// <param name="callers">How many there are.</param>
    /// <param name="call">What each calls.</param>
    /// <param name="stop">Stops them.</param>
    static Task LoadAsync(int callers, Func<Task> call, CancellationToken stop)
    {
        return Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            while (stop.IsCancellationRequested == false)
                await call();
        })));
    }

    /// <summary>
    /// The engines the pool runs.
    /// </summary>
    /// <param name="pool">The pool.</param>
    static int Count(NodeEnginePool pool)
    {
        return pool.GetStatistics().Engines.Count;
    }

    /// <summary>
    /// Under work that keeps the engines' threads busy, with acquisitions waiting, the pool tries a second engine and
    /// keeps it, since it does twice the work; and once there is no work, it retires it again.
    /// </summary>
    [TestMethod]
    public async Task A_second_engine_that_pays_stays_and_retires_when_idle()
    {
        await using var pool = Pool(idle: TimeSpan.FromMilliseconds(300));

        // One engine started, and a window begun with nothing waiting.
        await pool.RunAsync(Busy, exports => Task.FromResult((bool)exports.CallMethod("busy", 1)));
        await pool.AdaptAsync();
        Assert.AreEqual(1, Count(pool));

        using var stop = new CancellationTokenSource();
        var load = LoadAsync(8, () => pool.RunAsync(Busy, exports => Task.FromResult((bool)exports.CallMethod("busy", 20))), stop.Token);

        await Task.Delay(600);
        await pool.AdaptAsync();
        Assert.AreEqual(2, Count(pool), "No second engine was tried.");

        await Task.Delay(600);
        await pool.AdaptAsync();
        await Task.Delay(600);
        await pool.AdaptAsync();
        Assert.AreEqual(2, Count(pool), "The second engine was not kept.");

        stop.Cancel();
        await load;

        await Task.Delay(500);
        await pool.AdaptAsync();
        Assert.AreEqual(1, Count(pool), "The idle engine was not retired.");

        // What is left still works.
        Assert.IsTrue(await pool.RunAsync(Busy, exports => Task.FromResult((bool)exports.CallMethod("busy", 1))));
    }

    /// <summary>
    /// Where the work is held up outside the engines, a second engine does no more of it, and goes again after its
    /// trial; leases on it are allowed to finish.
    /// </summary>
    /// <remarks>
    /// The dependency lets through exactly as many calls a window as the test gives it permits for, and every window is
    /// the same length, so the throughput with two engines is the throughput with one to within the windows' lengths,
    /// whatever the machine: no engine could do more, and the climber must see that.
    /// </remarks>
    [TestMethod]
    public async Task A_second_engine_that_does_not_pay_goes_again()
    {
        await using var pool = Pool(idle: TimeSpan.FromMinutes(10));

        // A dependency with a capacity of its own, which more engines cannot get more out of.
        using var dependency = new SemaphoreSlim(0);
        var completed = 0;

        async Task<bool> Call()
        {
            await dependency.WaitAsync();
            Interlocked.Increment(ref completed);
            return true;
        }

        // One window of exactly the given calls, the same length as every other.
        async Task WindowAsync(int calls)
        {
            var began = Stopwatch.GetTimestamp();
            var before = Volatile.Read(ref completed);
            dependency.Release(calls);

            while (Volatile.Read(ref completed) < before + calls)
                await Task.Delay(10);

            var left = TimeSpan.FromMilliseconds(500) - TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency);
            if (left > TimeSpan.Zero)
                await Task.Delay(left);

            await pool.AdaptAsync();
        }

        await pool.RunAsync(() => Task.FromResult(true));
        await pool.AdaptAsync();

        using var stop = new CancellationTokenSource();
        var load = LoadAsync(8, () => pool.RunAsync(Call), stop.Token);

        // The baseline, with acquisitions waiting behind the leases held up at the dependency.
        await WindowAsync(24);
        Assert.AreEqual(2, Count(pool), "No second engine was tried.");

        // The trial: the same throughput, however many engines.
        await WindowAsync(24);
        await WindowAsync(24);
        Assert.AreEqual(1, Count(pool), "The second engine was kept though it did no more.");

        // What the retired engine held finishes, and what is left still works.
        await WindowAsync(24);
        stop.Cancel();
        dependency.Release(8);
        await load;
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
