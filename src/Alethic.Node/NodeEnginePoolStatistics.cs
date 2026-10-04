using System.Collections.Generic;

namespace Alethic.Node;

/// <summary>
/// A pool of Node engines, as it stood at one moment: what it has learned, and what it is carrying.
/// </summary>
public sealed class NodeEnginePoolStatistics
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="engines">Its engines.</param>
    /// <param name="queued">The acquisitions waiting for capacity.</param>
    public NodeEnginePoolStatistics(IReadOnlyList<NodeEngineStatistics> engines, int queued)
    {
        Engines = engines;
        Queued = queued;
    }

    /// <summary>
    /// Its engines.
    /// </summary>
    public IReadOnlyList<NodeEngineStatistics> Engines { get; }

    /// <summary>
    /// The acquisitions waiting for capacity.
    /// </summary>
    public int Queued { get; }

}
