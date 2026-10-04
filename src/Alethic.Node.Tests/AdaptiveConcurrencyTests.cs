using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// How an engine's limit follows its event-loop delay, apart from any engine.
/// </summary>
[TestClass]
public class AdaptiveConcurrencyTests
{

    /// <summary>
    /// Over the target, the limit falls by the ratio of the target to the delay.
    /// </summary>
    [TestMethod]
    public void Over_the_target_the_limit_falls_in_proportion()
    {
        Assert.AreEqual(12, AdaptiveConcurrency.Next(16, delay: 50, peak: 16, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// However far over the target, the limit falls by no more than half at a time.
    /// </summary>
    [TestMethod]
    public void The_limit_falls_by_no_more_than_half()
    {
        Assert.AreEqual(8, AdaptiveConcurrency.Next(16, delay: 1000, peak: 16, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// Just over the target, the limit still falls, by one.
    /// </summary>
    [TestMethod]
    public void Just_over_the_target_the_limit_falls_by_one()
    {
        Assert.AreEqual(3, AdaptiveConcurrency.Next(4, delay: 41, peak: 4, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// The limit falls no lower than its least.
    /// </summary>
    [TestMethod]
    public void The_limit_falls_no_lower_than_its_least()
    {
        Assert.AreEqual(2, AdaptiveConcurrency.Next(2, delay: 1000, peak: 2, min: 2, max: 32, target: 40));
    }

    /// <summary>
    /// Under the target, where the limit was what held the engine back, it rises by its square root, and at least one.
    /// </summary>
    [TestMethod]
    public void Under_the_target_a_reached_limit_rises()
    {
        Assert.AreEqual(2, AdaptiveConcurrency.Next(1, delay: 5, peak: 1, min: 1, max: 32, target: 40));
        Assert.AreEqual(6, AdaptiveConcurrency.Next(4, delay: 5, peak: 4, min: 1, max: 32, target: 40));
        Assert.AreEqual(20, AdaptiveConcurrency.Next(16, delay: 5, peak: 16, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// The limit rises no higher than its most.
    /// </summary>
    [TestMethod]
    public void The_limit_rises_no_higher_than_its_most()
    {
        Assert.AreEqual(18, AdaptiveConcurrency.Next(16, delay: 5, peak: 16, min: 1, max: 18, target: 40));
    }

    /// <summary>
    /// At the target exactly, the engine is keeping up: a reached limit rises.
    /// </summary>
    [TestMethod]
    public void At_the_target_a_reached_limit_rises()
    {
        Assert.AreEqual(6, AdaptiveConcurrency.Next(4, delay: 40, peak: 4, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// The least limit is one, and from one the limit still rises by one.
    /// </summary>
    [TestMethod]
    public void From_one_the_limit_rises_by_one()
    {
        Assert.AreEqual(1, AdaptiveConcurrency.Next(1, delay: 1000, peak: 1, min: 1, max: 32, target: 40));
        Assert.AreEqual(2, AdaptiveConcurrency.Next(1, delay: 1, peak: 1, min: 1, max: 32, target: 40));
    }

    /// <summary>
    /// Under the target, a limit nothing reached stays where it is: it says nothing about whether more would be
    /// welcome.
    /// </summary>
    [TestMethod]
    public void Under_the_target_an_unreached_limit_stays()
    {
        Assert.AreEqual(8, AdaptiveConcurrency.Next(8, delay: 5, peak: 3, min: 1, max: 32, target: 40));
    }

}
