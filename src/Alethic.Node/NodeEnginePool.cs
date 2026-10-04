using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Alethic.Node;

/// <summary>
/// A pool of embedded Node engines.
/// </summary>
/// <remarks>
/// This is a concrete facility, not an abstraction: it is libnode, on purpose, and code holding a
/// lease writes ordinary node-api-dotnet against the engine's runtime. Engines exist for
/// parallelism — JavaScript runs on one thread each, so throughput past a single core needs more of
/// them — and are created as demand requires, up to the configured size. A lease claims capacity on
/// one engine rather than the engine itself; many leases share an engine concurrently, because
/// everything awaited inside it yields to its event loop.
///
/// Each engine has a limit of its own, which an acquisition must find room under. In
/// <see cref="NodeEnginePoolMode.Fixed"/> mode it is <see cref="NodeEnginePoolOptions.MaxConcurrencyPerEngine"/>; in
/// <see cref="NodeEnginePoolMode.Adaptive"/> mode the pool learns it from the engine's event-loop delay. Where no
/// engine has room and no other may be started, the acquisition waits its turn, first come first served.
/// </remarks>
public sealed class NodeEnginePool : IAsyncDisposable
{

    readonly NodeEnginePoolOptions options;
    readonly IServiceProvider services;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// How often an adapting pool probes each engine's event-loop delay.
    /// </summary>
    static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(20);

    readonly object sync = new();
    readonly List<NodeEngine> engines = [];
    readonly LinkedList<Waiter> waiters = new();
    readonly CancellationTokenSource stopping = new();
    readonly Task? adapting;

    /// <summary>
    /// Engines being started, which count against the pool's size before they join it.
    /// </summary>
    int starting;

    /// <summary>
    /// When the pool last adapted, which began the window probes are counted in, as a <see cref="Stopwatch"/>
    /// timestamp. Under the lock.
    /// </summary>
    long windowStarted = Stopwatch.GetTimestamp();

    bool disposed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="options">How the pool is configured.</param>
    /// <param name="loggerFactory">Where the pool and its engines log.</param>
    /// <param name="services">What <see cref="NodeEnginePoolOptions.ConfigureEngine"/> is given.</param>
    public NodeEnginePool(IOptions<NodeEnginePoolOptions> options, ILoggerFactory loggerFactory, IServiceProvider services)
        : this(options?.Value!, loggerFactory, services)
    {

    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="options">How the pool is configured.</param>
    /// <param name="loggerFactory">Where the pool and its engines log.</param>
    /// <param name="services">What <see cref="NodeEnginePoolOptions.ConfigureEngine"/> is given.</param>
    public NodeEnginePool(NodeEnginePoolOptions options, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        this.services = services ?? throw new ArgumentNullException(nameof(services));

        if (options.MinConcurrencyPerEngine > options.MaxConcurrencyPerEngine)
            throw new ArgumentException("The least concurrency per engine is more than the most.", nameof(options));

        logger = loggerFactory.CreateLogger<NodeEnginePool>();

        if (options.Mode == NodeEnginePoolMode.Adaptive)
            adapting = Task.Run(() => AdaptLoopAsync(stopping.Token));
    }

    /// <summary>
    /// Takes a lease on whichever engine is carrying the least work for its limit.
    /// </summary>
    /// <remarks>
    /// The lease must be disposed, whether or not anything was done with it, or its capacity never
    /// returns.
    /// </remarks>
    /// <param name="cancellationToken">Abandons the acquisition.</param>
    /// <exception cref="TimeoutException">No capacity came free within <see cref="NodeEnginePoolOptions.AcquireTimeout"/>.</exception>
    public async Task<NodeEngineLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Waiter? waiter = null;
        var start = false;

        lock (sync)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().Name);

            if (TryClaim() is { } claimed)
                return new NodeEngineLease(this, claimed);

            // A free engine is always preferable to a new one: creating an engine costs a thread and
            // re-evaluating every module it will need, which is far more than queueing behind a
            // running one for the moment it takes to yield.
            if (engines.Count + starting < options.EngineCount)
            {
                starting++;
                start = true;
            }
            else
            {
                waiter = new Waiter();
                waiter.Node = waiters.AddLast(waiter);
            }
        }

        if (start)
            return new NodeEngineLease(this, await StartAndClaimAsync(cancellationToken));

        return new NodeEngineLease(this, await WaitAsync(waiter!, cancellationToken));
    }

    /// <summary>
    /// Acquires a lease, blocking while the pool grows an engine if it must.
    /// </summary>
    /// <remarks>
    /// The synchronous face of <see cref="AcquireAsync"/>: acquisition against a warm pool is
    /// immediate, and a cold one costs an engine's startup — a wait the caller has chosen to stand
    /// in rather than await.
    /// </remarks>
    public NodeEngineLease Acquire()
    {
        return AcquireAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs one piece of work against a module, on whichever engine is carrying the least, and
    /// returns the capacity when it completes.
    /// </summary>
    /// <remarks>
    /// The one-shot form: a single call needs no checkout ceremony. Take a lease instead when
    /// several steps must share one engine — successive one-shots may land on different engines,
    /// which different per-engine module state would notice — or when the claim must outlive the
    /// call, as a streamed result does.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    /// <param name="cancellationToken">Abandons the acquisition, and is handed to the module's loading.</param>
    public async Task<T> RunAsync<T>(NodeModuleSource module, Func<Microsoft.JavaScript.NodeApi.JSValue, Task<T>> work, CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(cancellationToken);
        return await lease.RunAsync(module, work, cancellationToken);
    }

    /// <summary>
    /// Runs one piece of work on an engine's thread, module-free, and returns the capacity when it
    /// completes.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work, on the engine's thread.</param>
    /// <param name="cancellationToken">Abandons the acquisition.</param>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(cancellationToken);
        return await lease.RunAsync(work);
    }

    /// <summary>
    /// Runs one piece of synchronous work against a module, blocking until it returns.
    /// </summary>
    /// <remarks>
    /// The synchronous one-shot, for work whose value is produced synchronously on the engine's
    /// thread. Promise-shaped work stays on <see cref="RunAsync{T}(NodeModuleSource, Func{Microsoft.JavaScript.NodeApi.JSValue, Task{T}}, CancellationToken)"/>:
    /// a promise settles when the engine's loop turns, which no blocked thread can wait out.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    public T Run<T>(NodeModuleSource module, Func<Microsoft.JavaScript.NodeApi.JSValue, T> work)
    {
        using var lease = Acquire();
        return lease.Run(module, work);
    }

    /// <summary>
    /// Runs one piece of synchronous work on an engine's thread, module-free, blocking until it
    /// returns.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work, on the engine's thread.</param>
    public T Run<T>(Func<T> work)
    {
        using var lease = Acquire();
        return lease.Run(work);
    }

    /// <summary>
    /// Brings the pool to its configured size, running the given warmup against each engine.
    /// </summary>
    /// <remarks>
    /// Standing engines up and evaluating modules both stall the engine they run on, so doing it
    /// during startup keeps the cost out of whichever request happens to arrive first.
    /// </remarks>
    /// <param name="warm">Run against each engine, on a lease of its own outside the engine's limit.</param>
    /// <param name="cancellationToken">Abandons the preparation.</param>
    public async Task PrepareAsync(Func<NodeEngineLease, Task>? warm = null, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (sync)
            {
                if (disposed)
                    throw new ObjectDisposedException(GetType().Name);

                if (engines.Count + starting >= options.EngineCount)
                    break;

                starting++;
            }

            await StartAndAddAsync(cancellationToken);
        }

        if (warm is null)
            return;

        foreach (var engine in Snapshot())
        {
            lock (sync)
                engine.InFlight++;

            await using var lease = new NodeEngineLease(this, engine);
            await warm(lease);
        }
    }

    /// <summary>
    /// The pool as it stands: each engine's limit and load, and the acquisitions waiting.
    /// </summary>
    public NodeEnginePoolStatistics GetStatistics()
    {
        lock (sync)
            return new NodeEnginePoolStatistics(engines.Select(i => new NodeEngineStatistics(i.InFlight, i.Limit, i.LoopDelay)).ToArray(), waiters.Count);
    }

    /// <summary>
    /// Returns a lease's capacity to the pool, and hands it to whoever is waiting.
    /// </summary>
    /// <param name="engine">The engine the lease was held against.</param>
    internal void Release(NodeEngine engine)
    {
        lock (sync)
            engine.InFlight--;

        Dispatch();
    }

    /// <summary>
    /// Reads every engine's event-loop delay, and sets each engine's limit from it. What the pool does by itself every
    /// <see cref="NodeEnginePoolOptions.AdaptInterval"/> in <see cref="NodeEnginePoolMode.Adaptive"/> mode.
    /// </summary>
    /// <remarks>
    /// An engine's delay over the window is the longest any of its probes waited, or the probe waiting now, where that
    /// has waited longer: an engine stuck for the whole window has finished no probe to say so.
    /// </remarks>
    /// <param name="cancellationToken">Abandons the reading.</param>
    internal Task AdaptAsync(CancellationToken cancellationToken = default)
    {
        var now = Stopwatch.GetTimestamp();

        lock (sync)
        {
            windowStarted = now;

            foreach (var engine in engines)
            {
                var waiting = engine.ProbeStarted == 0 ? 0 : Milliseconds(engine.ProbeStarted, now);
                var delay = Math.Max(engine.ProbeMax, waiting);

                var limit = AdaptiveConcurrency.Next(engine.Limit, delay, engine.Peak, options.MinConcurrencyPerEngine, options.MaxConcurrencyPerEngine, options.TargetEventLoopDelay.TotalMilliseconds);
                if (limit != engine.Limit)
                    logger.LogDebug("Node engine limit {From} to {To}: event-loop delay {Delay:0.0} ms, peak {Peak}.", engine.Limit, limit, delay, engine.Peak);

                engine.LoopDelay = delay;
                engine.Limit = limit;
                engine.Peak = engine.InFlight;
                engine.ProbeMax = 0;
            }
        }

        // A limit that rose has room for whoever is waiting.
        Dispatch();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Probes an engine's event-loop delay every <see cref="ProbeInterval"/>, one probe at a time, until the engine or
    /// the pool goes.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="cancellationToken">Stops it, with the pool.</param>
    async Task ProbeLoopAsync(NodeEngine engine, CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                var posted = Stopwatch.GetTimestamp();

                lock (sync)
                    engine.ProbeStarted = posted;

                var started = await engine.ProbeAsync();

                // Only what it waited within the current window counts in it: a probe that waited out the last one,
                // and is recorded only now, said what it had to say there.
                lock (sync)
                {
                    engine.ProbeStarted = 0;
                    engine.ProbeMax = Math.Max(engine.ProbeMax, Milliseconds(Math.Max(posted, windowStarted), started));
                }

                await Task.Delay(ProbeInterval, cancellationToken);
            }
        }
        catch (Exception)
        {
            // The engine or the pool has gone, which is the only way out.
        }
    }

    /// <summary>
    /// The time between two <see cref="Stopwatch"/> timestamps, in milliseconds.
    /// </summary>
    /// <param name="from">The earlier.</param>
    /// <param name="to">The later.</param>
    static double Milliseconds(long from, long to)
    {
        return Math.Max(0, to - from) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Adapts the limits every interval until the pool is disposed.
    /// </summary>
    /// <param name="cancellationToken">Stops it.</param>
    async Task AdaptLoopAsync(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            try
            {
                await Task.Delay(options.AdaptInterval, cancellationToken);
                await AdaptAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not adapt the Node engine pool's limits.");
            }
        }
    }

    /// <summary>
    /// Claims room on the engine carrying the least for its limit; nothing, where none has room. Under the lock.
    /// </summary>
    NodeEngine? TryClaim()
    {
        NodeEngine? best = null;
        var load = double.MaxValue;

        foreach (var engine in engines)
        {
            if (engine.InFlight >= engine.Limit)
                continue;

            var current = engine.InFlight / (double)engine.Limit;
            if (current < load)
            {
                best = engine;
                load = current;
            }
        }

        if (best is not null)
        {
            best.InFlight++;
            best.Peak = Math.Max(best.Peak, best.InFlight);
        }

        return best;
    }

    /// <summary>
    /// Hands whatever room the engines have to whoever is waiting, in turn.
    /// </summary>
    void Dispatch()
    {
        lock (sync)
        {
            while (waiters.First is { } first && TryClaim() is { } engine)
            {
                waiters.RemoveFirst();
                first.Value.Node = null;

                // Its continuation runs elsewhere, so completing it here does not run anything under the lock.
                first.Value.Completion.TrySetResult(engine);
            }
        }
    }

    /// <summary>
    /// Waits for room on an engine, until the acquisition times out or is cancelled.
    /// </summary>
    /// <param name="waiter">The waiting acquisition, already in line.</param>
    /// <param name="cancellationToken">Abandons it.</param>
    async Task<NodeEngine> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.AcquireTimeout);
        using var registration = timeout.Token.Register(() => Abandon(waiter, new TimeoutException($"No capacity in the Node engine pool within {options.AcquireTimeout}.")));
        using var cancellation = cancellationToken.Register(() => Abandon(waiter, new OperationCanceledException(cancellationToken)));

        return await waiter.Completion.Task;
    }

    /// <summary>
    /// Takes an acquisition out of line and fails it, unless it has already been given room.
    /// </summary>
    /// <param name="waiter">The waiting acquisition.</param>
    /// <param name="exception">Why it fails.</param>
    void Abandon(Waiter waiter, Exception exception)
    {
        lock (sync)
        {
            if (waiter.Node is not { } node)
                return;

            waiters.Remove(node);
            waiter.Node = null;
            waiter.Completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Starts an engine for an acquisition, adds it to the pool and claims room on it.
    /// </summary>
    /// <param name="cancellationToken">Abandons the start.</param>
    Task<NodeEngine> StartAndClaimAsync(CancellationToken cancellationToken)
    {
        return StartAndAddAsync(cancellationToken, claim: true);
    }

    /// <summary>
    /// Starts an engine, counted in <see cref="starting"/> by the caller, and adds it to the pool.
    /// </summary>
    /// <param name="cancellationToken">Abandons the start.</param>
    /// <param name="claim">Whether the starter claims room on it, as it joins, before anyone waiting is given any.</param>
    async Task<NodeEngine> StartAndAddAsync(CancellationToken cancellationToken, bool claim = false)
    {
        NodeEngine engine;

        try
        {
            engine = await StartAsync(cancellationToken);
        }
        catch
        {
            lock (sync)
                starting--;

            // Its place is free again: an acquisition waiting on the pool's size may start one itself.
            Dispatch();
            throw;
        }

        var late = false;

        lock (sync)
        {
            starting--;

            if (disposed)
            {
                late = true;
            }
            else
            {
                engines.Add(engine);

                if (claim)
                {
                    engine.InFlight++;
                    engine.Peak = Math.Max(engine.Peak, engine.InFlight);
                }
            }
        }

        if (late)
        {
            await engine.DisposeAsync();
            throw new ObjectDisposedException(GetType().Name);
        }

        if (options.Mode == NodeEnginePoolMode.Adaptive)
            _ = Task.Run(() => ProbeLoopAsync(engine, stopping.Token));

        // Whatever room the new engine has beyond what its starter takes goes to whoever is waiting.
        Dispatch();
        return engine;
    }

    /// <summary>
    /// Starts an engine and configures it, before it joins the pool.
    /// </summary>
    /// <param name="cancellationToken">Abandons the start.</param>
    async Task<NodeEngine> StartAsync(CancellationToken cancellationToken)
    {
        var engineLogger = loggerFactory.CreateLogger<NodeEngine>();
        engineLogger.LogDebug("Starting a Node engine.");

        var platform = NodeRuntimeHost.GetOrCreate(LibNodeLocator.Locate(options.LibNodePath));

        // Standing a runtime up is synchronous and takes a couple of hundred milliseconds, so it
        // is kept off whichever thread happened to ask for it.
        var engine = await Task.Run(() => new NodeEngine(platform, options.BaseDirectory ?? AppContext.BaseDirectory, engineLogger), cancellationToken);
        engine.Limit = options.MaxConcurrencyPerEngine;

        // Before it joins the pool, so nothing can be handed an engine whose setup has not run.
        // A failure disposes it rather than leaving a live runtime nothing owns.
        if (options.ConfigureEngine is { } configure)
        {
            try
            {
                lock (sync)
                    engine.InFlight++;

                await using var lease = new NodeEngineLease(this, engine);
                await configure(services, lease);
            }
            catch
            {
                await engine.DisposeAsync();
                throw;
            }
        }

        return engine;
    }

    /// <summary>
    /// Returns a stable copy of the current engines.
    /// </summary>
    NodeEngine[] Snapshot()
    {
        lock (sync)
            return [.. engines];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Waiter[] abandoned;

        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            abandoned = [.. waiters];
            waiters.Clear();
        }

        foreach (var waiter in abandoned)
            waiter.Completion.TrySetException(new ObjectDisposedException(GetType().Name));

        stopping.Cancel();
        if (adapting is not null)
            await adapting;

        foreach (var engine in Snapshot())
            await engine.DisposeAsync();

        stopping.Dispose();
    }

    /// <summary>
    /// An acquisition waiting for room on an engine.
    /// </summary>
    sealed class Waiter
    {

        /// <summary>
        /// Completed with the engine it is given room on, or failed.
        /// </summary>
        public TaskCompletionSource<NodeEngine> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Its place in line; <see langword="null"/> once it has left it. Under the pool's lock.
        /// </summary>
        public LinkedListNode<Waiter>? Node { get; set; }

    }

}
