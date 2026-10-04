namespace Alethic.Node;

/// <summary>
/// What an adaptive pool does about its number of engines, at the end of a window.
/// </summary>
enum EngineCountDecision
{

    /// <summary>
    /// Keeps the engines it has.
    /// </summary>
    Hold,

    /// <summary>
    /// Starts another engine, on trial.
    /// </summary>
    Grow,

    /// <summary>
    /// Retires an engine: the one on trial, which did not pay for itself.
    /// </summary>
    Shrink,

}
