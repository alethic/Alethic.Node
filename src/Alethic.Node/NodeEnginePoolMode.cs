namespace Alethic.Node;

/// <summary>
/// How a pool of Node engines sets its limits.
/// </summary>
public enum NodeEnginePoolMode
{

    /// <summary>
    /// The limits are what the options say: <see cref="NodeEnginePoolOptions.EngineCount"/> engines, each holding up to
    /// <see cref="NodeEnginePoolOptions.MaxConcurrencyPerEngine"/> leases.
    /// </summary>
    Fixed,

    /// <summary>
    /// The pool learns its limits from the work it is given, within the bounds the options set.
    /// </summary>
    /// <remarks>
    /// Each engine's limit follows its event-loop delay: it rises while the engine's thread keeps up with the work and
    /// the limit is what holds it back, and falls in proportion when the delay goes over
    /// <see cref="NodeEnginePoolOptions.TargetEventLoopDelay"/>.
    /// </remarks>
    Adaptive,

}
