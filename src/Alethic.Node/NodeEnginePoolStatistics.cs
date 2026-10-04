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
    /// <param name="overloaded">Whether the pool counts itself overloaded.</param>
    public NodeEnginePoolStatistics(IReadOnlyList<NodeEngineStatistics> engines, int queued, bool overloaded)
    {
        Engines = engines;
        Queued = queued;
        Overloaded = overloaded;
    }

    /// <summary>
    /// Its engines.
    /// </summary>
    public IReadOnlyList<NodeEngineStatistics> Engines { get; }

    /// <summary>
    /// The acquisitions waiting for capacity.
    /// </summary>
    public int Queued { get; }

    /// <summary>
    /// Whether the pool counts itself overloaded: acquisitions have gone on waiting, with none served in between, for
    /// longer than <see cref="NodeEnginePoolOptions.OverloadInterval"/>.
    /// </summary>
    public bool Overloaded { get; }

}
