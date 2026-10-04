using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// A pool that counts itself overloaded once acquisitions have gone on waiting, with none served in between, and then
/// fails fast what it cannot do.
/// </summary>
[TestClass]
public class NodeEnginePoolOverloadTests
{

    /// <summary>
    /// A pool of one engine, one lease at a time, overloaded after the line has stood for the interval given.
    /// </summary>
    /// <param name="interval">How long the line may stand before the pool is overloaded.</param>
    static NodeEnginePool Pool(TimeSpan interval)
    {
        return new NodeEnginePool(new NodeEnginePoolOptions()
        {
            MaxConcurrencyPerEngine = 1,
            AcquireTimeout = TimeSpan.FromSeconds(30),
            OverloadInterval = interval,
            OverloadAcquireTimeout = TimeSpan.FromMilliseconds(50),
        }, NullLoggerFactory.Instance, new NoServices());
    }

    /// <summary>
    /// Once the line has stood past the interval, an acquisition waits only the overload timeout, not the ordinary one;
    /// one that has waited longer than that is refused, not served, when capacity returns; and with the line gone, the
    /// pool serves as before.
    /// </summary>
    [TestMethod]
    public async Task An_overloaded_pool_fails_fast_and_refuses_what_waited_too_long()
    {
        await using var pool = Pool(TimeSpan.FromMilliseconds(200));

        var held = await pool.AcquireAsync();
        var early = pool.AcquireAsync();

        await Task.Delay(300);
        Assert.IsTrue(pool.GetStatistics().Overloaded);

        // Overloaded: a new acquisition gives up in the overload timeout, though the ordinary one is thirty seconds.
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => pool.AcquireAsync());
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"Gave up after {watch.Elapsed}.");

        // Capacity returns: the acquisition that waited through the overload is refused rather than served late.
        await held.DisposeAsync();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => early);

        // The line is gone, and with it the overload.
        Assert.IsFalse(pool.GetStatistics().Overloaded);
        await using var later = await pool.AcquireAsync();
    }

    /// <summary>
    /// A line that empties within the interval is a burst, which waiting rides out: what waited in it is served.
    /// </summary>
    [TestMethod]
    public async Task A_burst_is_waited_out()
    {
        await using var pool = Pool(TimeSpan.FromMilliseconds(500));

        var held = await pool.AcquireAsync();
        var waiting = pool.AcquireAsync();

        await Task.Delay(150);
        Assert.IsFalse(pool.GetStatistics().Overloaded);

        await held.DisposeAsync();
        await using var served = await waiting;
    }

    /// <summary>
    /// Without an interval, the pool is never overloaded, and every acquisition waits the ordinary timeout.
    /// </summary>
    [TestMethod]
    public async Task Without_an_interval_nothing_is_shed()
    {
        await using var pool = new NodeEnginePool(new NodeEnginePoolOptions()
        {
            MaxConcurrencyPerEngine = 1,
            AcquireTimeout = TimeSpan.FromSeconds(30),
        }, NullLoggerFactory.Instance, new NoServices());

        var held = await pool.AcquireAsync();
        var waiting = pool.AcquireAsync();

        await Task.Delay(300);
        Assert.IsFalse(pool.GetStatistics().Overloaded);

        await held.DisposeAsync();
        await using var served = await waiting;
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
