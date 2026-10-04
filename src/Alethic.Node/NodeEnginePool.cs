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

    readonly object sync = new();
    readonly List<NodeEngine> engines = [];
    readonly LinkedList<Waiter> waiters = new();
    readonly List<NodeEngine> retiring = [];
    readonly EngineCountClimber climber = new();
    readonly CancellationTokenSource stopping = new();
    readonly Task? adapting;

    /// <summary>
    /// How many engines the pool runs: <see cref="NodeEnginePoolOptions.EngineCount"/> where it is fixed, and where it
    /// adapts, what it has learned, from <see cref="NodeEnginePoolOptions.MinEngineCount"/>. Under the lock.
    /// </summary>
    int targetEngines;

    /// <summary>
    /// The most acquisitions waiting at once since the pool last adapted. Under the lock.
    /// </summary>
    int queuedPeak;

    /// <summary>
    /// The leases returned since the pool last adapted. Under the lock.
    /// </summary>
    long completed;

    /// <summary>
    /// When the line of acquisitions was last empty, as a <see cref="Stopwatch"/> timestamp. Under the lock.
    /// </summary>
    long lastEmpty = Stopwatch.GetTimestamp();

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
        if (options.Mode == NodeEnginePoolMode.Adaptive && options.MinEngineCount > options.EngineCount)
            throw new ArgumentException("The fewest engines is more than the most.", nameof(options));

        targetEngines = options.Mode == NodeEnginePoolMode.Adaptive ? options.MinEngineCount : options.EngineCount;

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
    /// <exception cref="TimeoutException">No capacity came free within <see cref="NodeEnginePoolOptions.AcquireTimeout"/>,
    /// or, the pool being overloaded, <see cref="NodeEnginePoolOptions.OverloadAcquireTimeout"/>.</exception>
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
            if (engines.Count + starting < targetEngines)
            {
                starting++;
                start = true;
            }
            else
            {
                var now = Stopwatch.GetTimestamp();

                // Empty until now.
                if (waiters.Count == 0)
                    lastEmpty = now;

                waiter = new Waiter(now, Overloaded(now) ? options.OverloadAcquireTimeout : options.AcquireTimeout);
                waiter.Node = waiters.AddLast(waiter);
                queuedPeak = Math.Max(queuedPeak, waiters.Count);
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

                if (engines.Count + starting >= targetEngines)
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
            return new NodeEnginePoolStatistics(engines.Select(i => new NodeEngineStatistics(i.InFlight, i.Limit, i.LoopDelay)).ToArray(), waiters.Count, Overloaded(Stopwatch.GetTimestamp()));
    }

    /// <summary>
    /// Returns a lease's capacity to the pool, and hands it to whoever is waiting.
    /// </summary>
    /// <param name="engine">The engine the lease was held against.</param>
    internal void Release(NodeEngine engine)
    {
        var stop = false;

        lock (sync)
        {
            engine.InFlight--;
            completed++;

            if (engine.InFlight == 0)
            {
                engine.IdleSince = Stopwatch.GetTimestamp();

                // A retired engine stops with the last lease it held.
                if (engine.Retiring && retiring.Remove(engine))
                    stop = true;
            }
        }

        if (stop)
            Stop(engine);

        Dispatch();
    }

    /// <summary>
    /// Reads every engine's event-loop delay, and sets each engine's limit from it. What the pool does by itself every
    /// <see cref="NodeEnginePoolOptions.AdaptInterval"/> in <see cref="NodeEnginePoolMode.Adaptive"/> mode.
    /// </summary>
    /// <remarks>
    /// An engine's delay over the window is how long the work posted to it waited before its thread ran it, measured
    /// from the leases themselves; see <see cref="NodeEngine.ReadWait"/>.
    /// </remarks>
    /// <param name="cancellationToken">Abandons the reading.</param>
    internal async Task AdaptAsync(CancellationToken cancellationToken = default)
    {
        var now = Stopwatch.GetTimestamp();
        var start = false;
        NodeEngine? stop = null;

        lock (sync)
        {
            var window = Milliseconds(windowStarted, now) / 1000;
            windowStarted = now;

            foreach (var engine in engines)
            {
                var delay = engine.ReadWait(now);

                var limit = AdaptiveConcurrency.Next(engine.Limit, delay, engine.Peak, options.MinConcurrencyPerEngine, options.MaxConcurrencyPerEngine, options.TargetEventLoopDelay.TotalMilliseconds);
                if (limit != engine.Limit)
                    logger.LogDebug("Node engine limit {From} to {To}: event-loop delay {Delay:0.0} ms, peak {Peak}.", engine.Limit, limit, delay, engine.Peak);

                engine.LoopDelay = delay;
                engine.Limit = limit;
                engine.Peak = engine.InFlight;
            }

            var throughput = window > 0 ? completed / window : 0;
            var saturated = queuedPeak > 0;
            completed = 0;
            queuedPeak = waiters.Count;

            switch (climber.Next(engines.Count + starting, options.EngineCount, throughput, saturated))
            {
                case EngineCountDecision.Grow:
                    logger.LogDebug("Trying another Node engine: {Throughput:0.0} leases a second with {Count}, and acquisitions waiting.", throughput, engines.Count);
                    targetEngines = Math.Min(options.EngineCount, targetEngines + 1);
                    if (engines.Count + starting < targetEngines)
                    {
                        starting++;
                        start = true;
                    }

                    break;

                case EngineCountDecision.Shrink:
                    logger.LogDebug("Retiring the Node engine on trial: {Throughput:0.0} leases a second with {Count} is no better.", throughput, engines.Count);
                    targetEngines = Math.Max(options.MinEngineCount, targetEngines - 1);
                    stop = RetireLeastLoaded();
                    break;

                default:
                    if (climber.OnTrial == false && Idlest(now) is { } idle)
                    {
                        logger.LogDebug("Retiring an idle Node engine.");
                        targetEngines = Math.Max(options.MinEngineCount, targetEngines - 1);
                        stop = Retire(idle);
                    }

                    break;
            }
        }

        if (stop is not null)
            Stop(stop);

        if (start)
        {
            try
            {
                await StartAndAddAsync(cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Could not start another Node engine.");
            }
        }

        // A limit that rose, or an engine that started, has room for whoever is waiting.
        Dispatch();
    }

    /// <summary>
    /// The engine longest idle past <see cref="NodeEnginePoolOptions.EngineIdleTimeout"/>, where the pool runs more
    /// than its fewest; nothing otherwise. Under the lock.
    /// </summary>
    /// <param name="now">The time, as a <see cref="Stopwatch"/> timestamp.</param>
    NodeEngine? Idlest(long now)
    {
        if (engines.Count <= options.MinEngineCount)
            return null;

        NodeEngine? idlest = null;

        foreach (var engine in engines)
            if (engine.InFlight == 0 && Milliseconds(engine.IdleSince, now) >= options.EngineIdleTimeout.TotalMilliseconds)
                if (idlest is null || engine.IdleSince < idlest.IdleSince)
                    idlest = engine;

        return idlest;
    }

    /// <summary>
    /// Retires the engine carrying the least, the newest of those carrying as little, where the pool runs more than its
    /// fewest. Under the lock.
    /// </summary>
    /// <returns>The engine, where it holds no lease and may be stopped now.</returns>
    NodeEngine? RetireLeastLoaded()
    {
        if (engines.Count <= options.MinEngineCount)
            return null;

        var least = engines[engines.Count - 1];
        for (var i = engines.Count - 2; i >= 0; i--)
            if (engines[i].InFlight < least.InFlight)
                least = engines[i];

        return Retire(least);
    }

    /// <summary>
    /// Takes an engine out of the pool: it takes no more leases, and stops once the last it holds is returned. Under the
    /// lock.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <returns>The engine, where it holds no lease and may be stopped now.</returns>
    NodeEngine? Retire(NodeEngine engine)
    {
        engines.Remove(engine);
        engine.Retiring = true;

        if (engine.InFlight == 0)
            return engine;

        retiring.Add(engine);
        return null;
    }

    /// <summary>
    /// Stops a retired engine, in the background.
    /// </summary>
    /// <param name="engine">The engine.</param>
    void Stop(NodeEngine engine)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await engine.DisposeAsync();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not stop a retired Node engine.");
            }
        });
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
    /// <remarks>
    /// Overloaded, an acquisition that has already waited longer than
    /// <see cref="NodeEnginePoolOptions.OverloadAcquireTimeout"/> is refused rather than served late, and the room goes
    /// to the next.
    /// </remarks>
    void Dispatch()
    {
        lock (sync)
        {
            var now = Stopwatch.GetTimestamp();

            while (waiters.First is { } first)
            {
                var waiter = first.Value;

                if (Overloaded(now) && Milliseconds(waiter.Enqueued, now) > options.OverloadAcquireTimeout.TotalMilliseconds)
                {
                    waiters.RemoveFirst();
                    waiter.Node = null;
                    waiter.Completion.TrySetException(Overload());
                    continue;
                }

                if (TryClaim() is not { } engine)
                    break;

                waiters.RemoveFirst();
                waiter.Node = null;

                // Its continuation runs elsewhere, so completing it here does not run anything under the lock.
                waiter.Completion.TrySetResult(engine);
            }

            if (waiters.Count == 0)
                lastEmpty = now;
        }
    }

    /// <summary>
    /// Whether acquisitions have gone on waiting, with none served in between, for longer than
    /// <see cref="NodeEnginePoolOptions.OverloadInterval"/>. Under the lock.
    /// </summary>
    /// <param name="now">The time, as a <see cref="Stopwatch"/> timestamp.</param>
    bool Overloaded(long now)
    {
        return options.OverloadInterval is { } interval && waiters.Count > 0 && Milliseconds(lastEmpty, now) > interval.TotalMilliseconds;
    }

    /// <summary>
    /// What an acquisition refused for overload fails with.
    /// </summary>
    TimeoutException Overload()
    {
        return new TimeoutException($"The Node engine pool is overloaded: no capacity within {options.OverloadAcquireTimeout}.");
    }

    /// <summary>
    /// Waits for room on an engine, until the acquisition times out or is cancelled.
    /// </summary>
    /// <param name="waiter">The waiting acquisition, already in line.</param>
    /// <param name="cancellationToken">Abandons it.</param>
    async Task<NodeEngine> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(waiter.Timeout);
        using var registration = timeout.Token.Register(() => Abandon(waiter, waiter.Timeout == options.AcquireTimeout ? new TimeoutException($"No capacity in the Node engine pool within {options.AcquireTimeout}.") : Overload()));
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

            if (waiters.Count == 0)
                lastEmpty = Stopwatch.GetTimestamp();
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
        engine.IdleSince = Stopwatch.GetTimestamp();

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

        NodeEngine[] retired;
        lock (sync)
        {
            retired = [.. retiring];
            retiring.Clear();
        }

        foreach (var engine in Snapshot().Concat(retired))
            await engine.DisposeAsync();

        stopping.Dispose();
    }

    /// <summary>
    /// An acquisition waiting for room on an engine.
    /// </summary>
    sealed class Waiter
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="enqueued">When it began waiting, as a <see cref="Stopwatch"/> timestamp.</param>
        /// <param name="timeout">How long it may wait.</param>
        public Waiter(long enqueued, TimeSpan timeout)
        {
            Enqueued = enqueued;
            Timeout = timeout;
        }

        /// <summary>
        /// When it began waiting, as a <see cref="Stopwatch"/> timestamp.
        /// </summary>
        public long Enqueued { get; }

        /// <summary>
        /// How long it may wait: <see cref="NodeEnginePoolOptions.AcquireTimeout"/>, or, where the pool was overloaded
        /// when it began, <see cref="NodeEnginePoolOptions.OverloadAcquireTimeout"/>.
        /// </summary>
        public TimeSpan Timeout { get; }

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
