namespace Alethic.Node;

/// <summary>
/// One engine of a pool, as it stood when its pool's statistics were taken.
/// </summary>
public sealed class NodeEngineStatistics
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="inFlight">The leases held against it.</param>
    /// <param name="limit">The most leases it may hold at once.</param>
    /// <param name="loopDelay">Its event-loop delay when last read, in milliseconds.</param>
    public NodeEngineStatistics(int inFlight, int limit, double loopDelay)
    {
        InFlight = inFlight;
        Limit = limit;
        LoopDelay = loopDelay;
    }

    /// <summary>
    /// The leases held against it.
    /// </summary>
    public int InFlight { get; }

    /// <summary>
    /// The most leases it may hold at once: fixed, or learned.
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// Its event-loop delay when last read, in milliseconds: the 99th percentile over the window before. Zero until the
    /// pool, adapting, has read it.
    /// </summary>
    public double LoopDelay { get; }

}
