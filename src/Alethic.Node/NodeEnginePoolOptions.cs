using System;
using System.Threading.Tasks;

namespace Alethic.Node;

/// <summary>
/// Configures one pool of embedded Node engines.
/// </summary>
public class NodeEnginePoolOptions
{

    int engineCount = 1;
    int minEngineCount = 1;
    TimeSpan engineIdleTimeout = TimeSpan.FromSeconds(30);
    int minConcurrencyPerEngine = 1;
    int maxConcurrencyPerEngine = 4;
    TimeSpan targetEventLoopDelay = TimeSpan.FromMilliseconds(40);
    TimeSpan adaptInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How the pool sets its limits. <see cref="NodeEnginePoolMode.Fixed"/> unless set.
    /// </summary>
    public NodeEnginePoolMode Mode { get; set; } = NodeEnginePoolMode.Fixed;

    /// <summary>
    /// Number of engines to run. Defaults to one. In <see cref="NodeEnginePoolMode.Adaptive"/> mode, the most the pool
    /// may run.
    /// </summary>
    /// <remarks>
    /// This must track the CPU the process is actually entitled to, and deliberately has no derived
    /// default: the processor count reports the host's cores rather than a container's quota, so
    /// deriving one misleads badly under orchestration. Spare CPU with too few engines goes unused,
    /// and engines beyond the available CPU only contend with each other.
    ///
    /// An adaptive pool finds out for itself how many of them are worth running, but never more than this, for the
    /// same reason.
    /// </remarks>
    public int EngineCount
    {
        get => engineCount;
        set => engineCount = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Engine count must be greater than zero.");
    }

    /// <summary>
    /// In <see cref="NodeEnginePoolMode.Adaptive"/> mode, the fewest engines the pool runs once it has started them.
    /// Defaults to one.
    /// </summary>
    /// <remarks>
    /// The pool starts this many as it needs them, as a fixed pool does. Above it, it adds an engine only where
    /// acquisitions have had to wait for capacity, and keeps it only where it raised how many leases the pool
    /// completes; it retires engines idle for <see cref="EngineIdleTimeout"/> back down to this.
    /// </remarks>
    public int MinEngineCount
    {
        get => minEngineCount;
        set => minEngineCount = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Engine count must be greater than zero.");
    }

    /// <summary>
    /// In <see cref="NodeEnginePoolMode.Adaptive"/> mode, how long an engine holds no lease before the pool retires it,
    /// where it runs more than <see cref="MinEngineCount"/>. Defaults to thirty seconds.
    /// </summary>
    public TimeSpan EngineIdleTimeout
    {
        get => engineIdleTimeout;
        set => engineIdleTimeout = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "The idle timeout must be greater than zero.");
    }

    /// <summary>
    /// Number of leases that may be held against one engine at a time. Defaults to four. In
    /// <see cref="NodeEnginePoolMode.Adaptive"/> mode, the most an engine's limit may rise to, and where it starts.
    /// </summary>
    /// <remarks>
    /// This is backpressure, not mutual exclusion. An engine overlaps many concurrent calls, since
    /// everything awaited inside it yields to its event loop; the gain flattens once concurrency
    /// covers the time spent waiting, and leaving it unbounded merely lets a slow dependency pile up
    /// work until memory runs out.
    /// </remarks>
    public int MaxConcurrencyPerEngine
    {
        get => maxConcurrencyPerEngine;
        set => maxConcurrencyPerEngine = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Concurrency must be greater than zero.");
    }

    /// <summary>
    /// In <see cref="NodeEnginePoolMode.Adaptive"/> mode, the least an engine's limit may fall to. Defaults to one.
    /// </summary>
    public int MinConcurrencyPerEngine
    {
        get => minConcurrencyPerEngine;
        set => minConcurrencyPerEngine = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Concurrency must be greater than zero.");
    }

    /// <summary>
    /// In <see cref="NodeEnginePoolMode.Adaptive"/> mode, the event-loop delay an engine is kept under: the 99th
    /// percentile over each <see cref="AdaptInterval"/>. Defaults to 40 milliseconds.
    /// </summary>
    /// <remarks>
    /// The delay is how late the engine's thread runs what is due on it, so it is what each request waits on top of
    /// its own work. Over the target, the engine's limit falls in proportion; under it, the limit may rise.
    /// </remarks>
    public TimeSpan TargetEventLoopDelay
    {
        get => targetEventLoopDelay;
        set => targetEventLoopDelay = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "The target delay must be greater than zero.");
    }

    /// <summary>
    /// In <see cref="NodeEnginePoolMode.Adaptive"/> mode, how often the pool reads its engines and adapts its limits.
    /// Defaults to one second. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> stops it adapting by itself.
    /// </summary>
    public TimeSpan AdaptInterval
    {
        get => adaptInterval;
        set => adaptInterval = value > TimeSpan.Zero || value == System.Threading.Timeout.InfiniteTimeSpan ? value : throw new ArgumentOutOfRangeException(nameof(value), "The interval must be greater than zero.");
    }

    /// <summary>
    /// How long an acquisition may wait for capacity before it is abandoned. Defaults to ten seconds.
    /// </summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs once against each engine as it starts, before anything else is given it.
    /// </summary>
    /// <remarks>
    /// Whatever an engine needs to be true before it answers anything: a global installed, a module
    /// warmed, a polyfill applied. It is handed a lease, which is the ordinary way to be on an
    /// engine's thread — inside it you are writing node-api-dotnet against that runtime, exactly as
    /// you would against a lease taken from the pool.
    ///
    /// Per engine, not per acquisition, and the engine does not join the pool until this returns. A
    /// throw fails the engine and disposes it rather than publishing one whose setup did not
    /// complete, on the same reasoning that a handler which cannot prepare fails the deployment: an
    /// engine that could not be configured is broken, and serving from it is worse than not having
    /// it.
    ///
    /// The provider comes with it because engines are allocated lazily, over the life of the
    /// process: one may stand up long after this was configured, and it should be configured against
    /// the container as it is then rather than against whatever was resolved and captured earlier.
    /// It is the root, engines being singletons — anything scoped is the delegate's own to scope.
    /// </remarks>
    public Func<IServiceProvider, NodeEngineLease, Task>? ConfigureEngine { get; set; }

    /// <summary>
    /// Path to the native Node library. Unset, it is found where the Microsoft.JavaScript.LibNode packages put it: under
    /// the application's <c>runtimes/&lt;rid&gt;/native</c>, beside a published application, or, for ASP.NET on .NET
    /// Framework, under the site's <c>bin</c>.
    /// </summary>
    public string? LibNodePath { get; set; }

    /// <summary>
    /// Root for Node's package resolution. Defaults to the application's base directory.
    /// </summary>
    /// <remarks>
    /// A module loaded by absolute path does not need this, and a self-contained bundle resolves
    /// nothing outward, so it matters only where a module reaches a <c>node_modules</c> directory.
    /// Node looks here and in parent directories, as it would for any program rooted at this path.
    /// </remarks>
    public string? BaseDirectory { get; set; }

}
