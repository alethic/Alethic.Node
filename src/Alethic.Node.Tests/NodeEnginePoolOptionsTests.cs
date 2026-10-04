using System;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// What a pool's options refuse, before any engine exists.
/// </summary>
[TestClass]
public class NodeEnginePoolOptionsTests
{

    /// <summary>
    /// Each count and span must be positive.
    /// </summary>
    [TestMethod]
    public void Counts_and_spans_must_be_positive()
    {
        var options = new NodeEnginePoolOptions();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.EngineCount = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MinEngineCount = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MinConcurrencyPerEngine = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MaxConcurrencyPerEngine = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.TargetEventLoopDelay = TimeSpan.Zero);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.EngineIdleTimeout = TimeSpan.Zero);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.AdaptInterval = TimeSpan.Zero);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MemoryLoadLimit = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MemoryLoadLimit = 1.5);
    }

    /// <summary>
    /// The adapt interval may be infinite, which stops the pool adapting by itself.
    /// </summary>
    [TestMethod]
    public void The_adapt_interval_may_be_infinite()
    {
        var options = new NodeEnginePoolOptions() { AdaptInterval = System.Threading.Timeout.InfiniteTimeSpan };
        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, options.AdaptInterval);
    }

    /// <summary>
    /// A pool refuses bounds whose least is more than their most.
    /// </summary>
    [TestMethod]
    public void A_pool_refuses_inverted_bounds()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new NodeEnginePool(
            new NodeEnginePoolOptions() { MinConcurrencyPerEngine = 4, MaxConcurrencyPerEngine = 2 },
            NullLoggerFactory.Instance, new NoServices()));

        Assert.ThrowsExactly<ArgumentException>(() => new NodeEnginePool(
            new NodeEnginePoolOptions() { Mode = NodeEnginePoolMode.Adaptive, MinEngineCount = 3, EngineCount = 2 },
            NullLoggerFactory.Instance, new NoServices()));
    }

    /// <summary>
    /// A fixed pool does not mind the engine bounds, which it does not use.
    /// </summary>
    [TestMethod]
    public async System.Threading.Tasks.Task A_fixed_pool_ignores_the_engine_bounds()
    {
        await using var pool = new NodeEnginePool(
            new NodeEnginePoolOptions() { MinEngineCount = 3, EngineCount = 2 },
            NullLoggerFactory.Instance, new NoServices());
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
