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
    /// <param name="heapUsed">Its heap in use, in bytes.</param>
    /// <param name="heapTotal">Its heap committed, in bytes.</param>
    /// <param name="heapLimit">The most its heap may grow to, in bytes.</param>
    /// <param name="externalMemory">The memory outside its heap that its objects hold, in bytes.</param>
    public NodeEngineStatistics(int inFlight, int limit, double loopDelay, long heapUsed, long heapTotal, long heapLimit, long externalMemory)
    {
        InFlight = inFlight;
        Limit = limit;
        LoopDelay = loopDelay;
        HeapUsed = heapUsed;
        HeapTotal = heapTotal;
        HeapLimit = heapLimit;
        ExternalMemory = externalMemory;
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
    /// Its event-loop delay when last read, in milliseconds: how long work posted to it waited before its thread ran
    /// it, over the window before. Zero until the pool, adapting, has read it.
    /// </summary>
    public double LoopDelay { get; }

    /// <summary>
    /// Its heap in use, in bytes, as of its last work.
    /// </summary>
    /// <remarks>
    /// An engine's memory is V8's, managed by V8's own collector, and the .NET garbage collector sees none of it: a
    /// process's managed heap says nothing about what its engines hold. These figures are read on the engine's thread
    /// as work starts there, at most every quarter second of its time; an idle engine reports them as they were when
    /// it last worked.
    /// </remarks>
    public long HeapUsed { get; }

    /// <summary>
    /// Its heap committed, in bytes, as of its last work: what V8 has reserved for it, in use or not.
    /// </summary>
    public long HeapTotal { get; }

    /// <summary>
    /// The most its heap may grow to, in bytes: V8's limit for the engine, past which it fails rather than grows.
    /// </summary>
    public long HeapLimit { get; }

    /// <summary>
    /// The memory outside its heap that its objects hold, in bytes, as of its last work: buffers and the like.
    /// </summary>
    public long ExternalMemory { get; }

}
