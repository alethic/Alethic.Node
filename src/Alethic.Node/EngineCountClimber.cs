namespace Alethic.Node;

/// <summary>
/// Learns how many engines are worth running, by trying one more and keeping it only where it pays.
/// </summary>
/// <remarks>
/// Hill climbing, as the .NET thread pool injects threads: a change is tried, its effect on throughput measured, and
/// kept or undone. Here the change is one more engine, tried only where acquisitions have had to wait for capacity,
/// since otherwise it could not help; and the measure is how many leases the pool completes a second. An engine adds
/// throughput where there is a core for it and the work is the engines' own; where the work is held up elsewhere, or
/// the cores are spoken for, another engine is only another thread contending, and it goes again.
///
/// A new engine is judged after <see cref="trialWindows"/> windows, the first of them taken up with its starting and
/// its modules' loading. One undone is not tried again for <see cref="cooldownWindows"/> windows, so a pool at its
/// best does not keep paying to find that out.
/// </remarks>
sealed class EngineCountClimber
{

    readonly int trialWindows;
    readonly int cooldownWindows;
    readonly double minimumGain;

    int trialLeft;
    double baseline;
    int cooldown;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="trialWindows">How many windows a new engine has before it is judged.</param>
    /// <param name="cooldownWindows">How many windows pass after one is undone before another is tried.</param>
    /// <param name="minimumGain">The rise in throughput, as a fraction, a new engine must bring to stay.</param>
    public EngineCountClimber(int trialWindows = 2, int cooldownWindows = 10, double minimumGain = 0.1)
    {
        this.trialWindows = trialWindows;
        this.cooldownWindows = cooldownWindows;
        this.minimumGain = minimumGain;
    }

    /// <summary>
    /// Whether an engine is on trial.
    /// </summary>
    public bool OnTrial => trialLeft > 0;

    /// <summary>
    /// What to do about the number of engines, at the end of a window.
    /// </summary>
    /// <param name="count">The engines the pool runs, and is starting.</param>
    /// <param name="max">The most it may run.</param>
    /// <param name="throughput">The leases it completed a second over the window.</param>
    /// <param name="saturated">Whether acquisitions had to wait for capacity in the window.</param>
    public EngineCountDecision Next(int count, int max, double throughput, bool saturated)
    {
        if (trialLeft > 0)
        {
            trialLeft--;
            if (trialLeft > 0)
                return EngineCountDecision.Hold;

            if (throughput >= baseline * (1 + minimumGain))
                return EngineCountDecision.Hold;

            cooldown = cooldownWindows;
            return EngineCountDecision.Shrink;
        }

        if (cooldown > 0)
        {
            cooldown--;
            return EngineCountDecision.Hold;
        }

        if (saturated && count < max)
        {
            baseline = throughput;
            trialLeft = trialWindows;
            return EngineCountDecision.Grow;
        }

        return EngineCountDecision.Hold;
    }

}
