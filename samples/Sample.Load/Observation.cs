using Alethic.Node;

namespace Sample.Load;

/// <summary>
/// What was seen of the pool over a phase: the extremes of each figure, and how the phase ended.
/// </summary>
public sealed class Observation
{

    /// <summary>
    /// The most engines running at once.
    /// </summary>
    public int MaxEngines { get; private set; }

    /// <summary>
    /// The fewest engines running at once.
    /// </summary>
    public int MinEngines { get; private set; } = int.MaxValue;

    /// <summary>
    /// The lowest any engine's limit went.
    /// </summary>
    public int MinLimit { get; private set; } = int.MaxValue;

    /// <summary>
    /// The highest any engine's limit went.
    /// </summary>
    public int MaxLimit { get; private set; }

    /// <summary>
    /// The latest any engine's event loop ran, in milliseconds.
    /// </summary>
    public double MaxLoopDelay { get; private set; }

    /// <summary>
    /// Whether the pool counted itself overloaded at any point.
    /// </summary>
    public bool OverloadedSeen { get; private set; }

    /// <summary>
    /// The most any engine's heap held, in bytes.
    /// </summary>
    public long MaxHeapUsed { get; private set; }

    /// <summary>
    /// The pool as the phase ended.
    /// </summary>
    public NodeEnginePoolStatistics Final { get; private set; } = new([], 0, false, null);

    /// <summary>
    /// The calls completed over the phase.
    /// </summary>
    public long Completed { get; set; }

    /// <summary>
    /// The calls refused over the phase, for want of capacity.
    /// </summary>
    public long Refused { get; set; }

    /// <summary>
    /// The calls that failed over the phase for any other reason.
    /// </summary>
    public long Failed { get; set; }

    /// <summary>
    /// Takes in a reading of the pool.
    /// </summary>
    /// <param name="statistics">The reading.</param>
    public void Observe(NodeEnginePoolStatistics statistics)
    {
        Final = statistics;
        MaxEngines = Math.Max(MaxEngines, statistics.Engines.Count);
        MinEngines = Math.Min(MinEngines, statistics.Engines.Count);
        OverloadedSeen |= statistics.Overloaded;

        foreach (var engine in statistics.Engines)
        {
            MinLimit = Math.Min(MinLimit, engine.Limit);
            MaxLimit = Math.Max(MaxLimit, engine.Limit);
            MaxLoopDelay = Math.Max(MaxLoopDelay, engine.LoopDelay);
            MaxHeapUsed = Math.Max(MaxHeapUsed, engine.HeapUsed);
        }
    }

}
