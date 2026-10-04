using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// How an adaptive pool decides how many engines to run, apart from any engine.
/// </summary>
[TestClass]
public class EngineCountClimberTests
{

    /// <summary>
    /// Where acquisitions waited and there is room for another, it is tried.
    /// </summary>
    [TestMethod]
    public void Waiting_acquisitions_try_another_engine()
    {
        var climber = new EngineCountClimber();
        Assert.AreEqual(EngineCountDecision.Grow, climber.Next(count: 1, max: 4, throughput: 100, saturated: true));
        Assert.IsTrue(climber.OnTrial);
    }

    /// <summary>
    /// Where nothing waited, there is nothing another engine could help with.
    /// </summary>
    [TestMethod]
    public void Nothing_waiting_tries_nothing()
    {
        var climber = new EngineCountClimber();
        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 1, max: 4, throughput: 100, saturated: false));
    }

    /// <summary>
    /// At its most, the pool tries no more.
    /// </summary>
    [TestMethod]
    public void At_the_most_tries_nothing()
    {
        var climber = new EngineCountClimber();
        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 4, max: 4, throughput: 100, saturated: true));
    }

    /// <summary>
    /// An engine that raised throughput enough stays, after its trial; and with acquisitions still waiting, another is
    /// tried.
    /// </summary>
    [TestMethod]
    public void An_engine_that_pays_stays_and_another_is_tried()
    {
        var climber = new EngineCountClimber(trialWindows: 2, cooldownWindows: 10, minimumGain: 0.1);
        climber.Next(count: 1, max: 4, throughput: 100, saturated: true);

        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 2, max: 4, throughput: 120, saturated: true));
        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 2, max: 4, throughput: 190, saturated: true));
        Assert.IsFalse(climber.OnTrial);
        Assert.AreEqual(EngineCountDecision.Grow, climber.Next(count: 2, max: 4, throughput: 190, saturated: true));
    }

    /// <summary>
    /// An engine that did not raise throughput enough goes again, and none is tried until the cooldown has passed.
    /// </summary>
    [TestMethod]
    public void An_engine_that_does_not_pay_goes_and_the_climber_waits()
    {
        var climber = new EngineCountClimber(trialWindows: 2, cooldownWindows: 3, minimumGain: 0.1);
        climber.Next(count: 1, max: 4, throughput: 100, saturated: true);

        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 2, max: 4, throughput: 100, saturated: true));
        Assert.AreEqual(EngineCountDecision.Shrink, climber.Next(count: 2, max: 4, throughput: 105, saturated: true));

        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 1, max: 4, throughput: 100, saturated: true));
        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 1, max: 4, throughput: 100, saturated: true));
        Assert.AreEqual(EngineCountDecision.Hold, climber.Next(count: 1, max: 4, throughput: 100, saturated: true));
        Assert.AreEqual(EngineCountDecision.Grow, climber.Next(count: 1, max: 4, throughput: 100, saturated: true));
    }

}
