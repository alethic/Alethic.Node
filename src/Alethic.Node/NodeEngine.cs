using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;

namespace Alethic.Node;

/// <summary>
/// One embedded Node runtime on its own thread.
/// </summary>
sealed class NodeEngine : IAsyncDisposable
{

    /// <summary>
    /// Gives the runtime a <c>require</c> that resolves files, and defuses unhandled rejections.
    /// </summary>
    /// <remarks>
    /// <c>module.createRequire</c> is the documented way to get a working loader in an embedded
    /// runtime, and it is Node's own: it resolves paths and packages by Node's rules and caches what
    /// it loads in <c>require.cache</c>, keyed by resolved filename. That is the whole of this
    /// library's module handling — a module gets one instance per runtime, with its module scope
    /// intact, because that is what <c>require</c> means. Since Node 22.12 it loads an ES module too,
    /// where the module's graph has no top-level await; one that has is imported instead, by
    /// <see cref="RunAsync{T}(NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/>.
    ///
    /// The rejection handler is not optional. A promise rejection with no handler yet attached — a
    /// gap that async .NET callers cannot always avoid — is fatal by Node's default, and here fatal
    /// means the whole process. The rejection still reaches whoever awaits the promise; this only
    /// stops the gap itself from killing anything.
    /// </remarks>
    const string MainScript =
        "globalThis.require = require('module').createRequire(process.execPath);\n" +
        "process.on('unhandledRejection', (reason) => {\n" +
        "    console.error('[Alethic.Node] unhandled promise rejection:', reason);\n" +
        "});\n" +
        TrackTimersScript +
        HeapScript;

    /// <summary>
    /// Keeps hold of the timers an application schedules, so they can be let go of on the way out.
    /// </summary>
    /// <remarks>
    /// Node does not report timers among its active handles, so there is no asking it later what is
    /// still pending: the only way to know is to have watched them being made. Hence the wrappers,
    /// which are otherwise faithful — they return what the originals return and pass everything
    /// through.
    ///
    /// The bookkeeping stays bounded. A timeout drops itself when it fires, an interval when it is
    /// cleared, so a long-running engine holds only what is genuinely still scheduled.
    /// </remarks>
    const string TrackTimersScript = """
        (() => {
            const pending = new Set();
            const setTimeoutOriginal = globalThis.setTimeout;
            const setIntervalOriginal = globalThis.setInterval;
            const clearTimeoutOriginal = globalThis.clearTimeout;
            const clearIntervalOriginal = globalThis.clearInterval;

            globalThis.setTimeout = function (callback, delay, ...args) {
                let handle;
                const once = typeof callback === 'function'
                    ? function (...called) { pending.delete(handle); return callback.apply(this, called); }
                    : callback;
                handle = setTimeoutOriginal(once, delay, ...args);
                if (handle && typeof handle.unref === 'function') pending.add(handle);
                return handle;
            };

            globalThis.setInterval = function (callback, delay, ...args) {
                const handle = setIntervalOriginal(callback, delay, ...args);
                if (handle && typeof handle.unref === 'function') pending.add(handle);
                return handle;
            };

            globalThis.clearTimeout = function (handle) { pending.delete(handle); return clearTimeoutOriginal(handle); };
            globalThis.clearInterval = function (handle) { pending.delete(handle); return clearIntervalOriginal(handle); };

            globalThis.__alethicRelease = function () {
                for (const handle of pending) {
                    try {
                        handle.unref();
                    } catch {
                    }
                }

                pending.clear();
            };
        })();
        """;

    /// <summary>
    /// Reads the engine's heap, for the pool's statistics.
    /// </summary>
    /// <remarks>
    /// V8's own figures, in bytes: the heap in use, the heap committed, the most the heap may grow to, and the memory
    /// outside the heap that JavaScript objects hold, such as buffers. The .NET garbage collector sees none of it: an
    /// engine's memory is V8's, managed by V8's collector, and this is the only account of it.
    /// </remarks>
    const string HeapScript = """
        globalThis.__alethicHeap = function () {
            const s = require('v8').getHeapStatistics();
            return [s.used_heap_size, s.total_heap_size, s.heap_size_limit, s.external_memory ?? 0];
        };
        """;

    /// <summary>
    /// How much of the engine's time passes between readings of its heap.
    /// </summary>
    static readonly long HeapSampleInterval = System.Diagnostics.Stopwatch.Frequency / 4;

    /// <summary>
    /// Unreferences every handle the runtime still holds.
    /// </summary>
    /// <remarks>
    /// There is no public way to enumerate what is keeping a loop alive, so this uses the internal
    /// accessor and tolerates its absence: a runtime that does not offer it simply closes the slow
    /// way rather than failing.
    /// </remarks>
    const string ReleaseScript = """
        (() => {
            globalThis.__alethicRelease?.();

            // Sockets and the like, which Node does report.
            const handles = typeof process._getActiveHandles === 'function' ? process._getActiveHandles() : [];
            for (const handle of handles) {
                try {
                    handle?.unref?.();
                } catch {
                }
            }
        })();
        """;

    readonly NodeEmbeddingThreadRuntime runtime;
    readonly ILogger logger;

    /// <summary>
    /// The modules <c>require</c> refused as ES modules that await at their top level, by path, which are imported
    /// instead. On the engine's thread alone.
    /// </summary>
    readonly HashSet<string> imported = new(StringComparer.Ordinal);

    bool disposed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="platform"></param>
    /// <param name="baseDirectory">Root for Node's package resolution.</param>
    /// <param name="logger"></param>
    public NodeEngine(NodeEmbeddingPlatform platform, string baseDirectory, ILogger logger)
    {
        if (platform is null)
            throw new ArgumentNullException(nameof(platform));
        if (baseDirectory is null)
            throw new ArgumentNullException(nameof(baseDirectory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        runtime = platform.CreateThreadRuntime(baseDirectory, new NodeEmbeddingRuntimeSettings()
        {
            MainScript = MainScript,

            // The inspector agent may only start once per process, and a second runtime attempting it
            // aborts the process from native code rather than raising anything catchable. Nothing here
            // needs it.
            RuntimeFlags = NodejsRuntime.NodeEmbeddingRuntimeFlags.NoCreateInspector |
                NodejsRuntime.NodeEmbeddingRuntimeFlags.NoStartDebugSignalHandler,
        });
    }

    /// <summary>
    /// The engine's id: stable for its life, and never reused in the process. Set by the pool as it starts.
    /// </summary>
    internal int Id;

    /// <summary>
    /// The number of leases currently held against this engine. Kept under the pool's lock.
    /// </summary>
    internal int InFlight;

    /// <summary>
    /// The number of leases the pool may hold against this engine at once. Kept under the pool's lock.
    /// </summary>
    internal int Limit;

    /// <summary>
    /// The most leases held against this engine at once since the pool last adapted its limit. Kept under the pool's
    /// lock.
    /// </summary>
    internal int Peak;

    /// <summary>
    /// The engine's event-loop delay as the pool last read it, in milliseconds. Kept under the pool's lock.
    /// </summary>
    internal double LoopDelay;

    /// <summary>
    /// When the engine last held no lease, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp. Kept under the
    /// pool's lock.
    /// </summary>
    internal long IdleSince;

    /// <summary>
    /// Whether the pool has retired the engine: it takes no more leases, and stops once the last it holds is returned.
    /// Kept under the pool's lock.
    /// </summary>
    internal bool Retiring;

    /// <summary>
    /// Keeps the stamps: the work posted to the engine's thread and not yet started, and the longest any waited.
    /// </summary>
    readonly object stamps = new();

    /// <summary>
    /// When each piece of work still waiting for the engine's thread was posted, as
    /// <see cref="System.Diagnostics.Stopwatch"/> timestamps, oldest first. Under <see cref="stamps"/>.
    /// </summary>
    readonly LinkedList<long> waiting = new();

    /// <summary>
    /// The longest any work waited for the engine's thread since the pool last read it, in milliseconds. Under
    /// <see cref="stamps"/>.
    /// </summary>
    double waitMax;

    /// <summary>
    /// When the pool last read the wait, which began the window the wait is counted in, as a
    /// <see cref="System.Diagnostics.Stopwatch"/> timestamp. Under <see cref="stamps"/>.
    /// </summary>
    long windowStarted = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>
    /// The engine's heap as last read, in bytes: in use, committed, its limit, and the memory outside it. Under
    /// <see cref="stamps"/>.
    /// </summary>
    long heapUsed, heapTotal, heapLimit, externalMemory;

    /// <summary>
    /// When the heap was last read, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp; zero while it never was.
    /// Under <see cref="stamps"/>.
    /// </summary>
    long heapSampled;

    /// <summary>
    /// The engine's heap as of its last work, in bytes: in use, committed, its limit, and the memory outside it that its
    /// objects hold. Zero until it has worked.
    /// </summary>
    /// <remarks>
    /// Read on the engine's own thread as work starts there, at most every quarter second of its time, rather than
    /// asked for: a reading posted to a saturated engine would wait behind the saturation it was meant to show.
    /// </remarks>
    internal (long Used, long Total, long Limit, long External) Heap
    {
        get
        {
            lock (stamps)
                return (heapUsed, heapTotal, heapLimit, externalMemory);
        }
    }

    /// <summary>
    /// Reads the heap, where it has not been read lately. On the engine's thread.
    /// </summary>
    /// <param name="now">The time, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    void SampleHeap(long now)
    {
        lock (stamps)
        {
            if (heapSampled != 0 && now - heapSampled < HeapSampleInterval)
                return;

            heapSampled = now;
        }

        var figures = JSValue.Global["__alethicHeap"].Call();
        var used = (long)(double)figures[0];
        var total = (long)(double)figures[1];
        var limit = (long)(double)figures[2];
        var external = (long)(double)figures[3];

        lock (stamps)
        {
            heapUsed = used;
            heapTotal = total;
            heapLimit = limit;
            externalMemory = external;
        }
    }

    /// <summary>
    /// The engine's event-loop delay over the window just ended, in milliseconds, and the start of the next.
    /// </summary>
    /// <remarks>
    /// How long work posted to the engine waited before its thread ran it: what any lease's work waits, measured from
    /// the leases themselves, which the pool posts anyway. The thread is one, so what is posted waits behind whatever
    /// it is busy with, and only that. Work still waiting counts for as long as it has waited so far: an engine stuck
    /// for the whole window has started nothing that could say so. Only the part of a wait within the window counts in
    /// it.
    /// </remarks>
    /// <param name="now">The time, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    internal double ReadWait(long now)
    {
        lock (stamps)
        {
            var delay = waitMax;
            if (waiting.First is { } oldest)
                delay = Math.Max(delay, Milliseconds(Math.Max(oldest.Value, windowStarted), now));

            waitMax = 0;
            windowStarted = now;
            return delay;
        }
    }

    /// <summary>
    /// Stamps work as posted to the engine's thread.
    /// </summary>
    LinkedListNode<long> Posted()
    {
        lock (stamps)
            return waiting.AddLast(System.Diagnostics.Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Stamps work as started on the engine's thread, records what it waited, and reads the heap now and then.
    /// </summary>
    /// <param name="posted">Its stamp from <see cref="Posted"/>.</param>
    void Started(LinkedListNode<long> posted)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();

        lock (stamps)
        {
            if (posted.List == waiting)
                waiting.Remove(posted);

            waitMax = Math.Max(waitMax, Milliseconds(Math.Max(posted.Value, windowStarted), now));
        }

        SampleHeap(now);
    }

    /// <summary>
    /// Takes back the stamp of work the runtime refused, so that it is not counted as waiting.
    /// </summary>
    /// <remarks>
    /// Work the runtime took but never starts, which only a runtime being torn down does, keeps its stamp: the engine is
    /// going, and nothing reads it again.
    /// </remarks>
    /// <param name="posted">Its stamp from <see cref="Posted"/>.</param>
    void Unposted(LinkedListNode<long> posted)
    {
        lock (stamps)
            if (posted.List == waiting)
                waiting.Remove(posted);
    }

    /// <summary>
    /// The time between two <see cref="System.Diagnostics.Stopwatch"/> timestamps, in milliseconds.
    /// </summary>
    /// <param name="from">The earlier.</param>
    /// <param name="to">The later.</param>
    static double Milliseconds(long from, long to)
    {
        return Math.Max(0, to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>
    /// Runs work on this engine's thread, stamped.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work.</param>
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        var posted = Posted();
        try
        {
            return runtime.RunAsync(() =>
            {
                Started(posted);
                return work();
            });
        }
        catch
        {
            Unposted(posted);
            throw;
        }
    }

    /// <summary>
    /// Runs synchronous work on this engine's thread, stamped, blocking until it returns.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work.</param>
    public T Run<T>(Func<T> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        var posted = Posted();
        try
        {
            return runtime.Run(() =>
            {
                Started(posted);
                return work();
            });
        }
        catch
        {
            Unposted(posted);
            throw;
        }
    }

    /// <summary>
    /// Runs work against a module's exports on this engine's thread, loading the module first if
    /// Node has not already.
    /// </summary>
    /// <remarks>
    /// Nothing is cached here, and nothing is referenced. <c>require</c> caches by resolved filename
    /// in <c>require.cache</c>, which is exactly the identity a module has in any other Node program
    /// — one instance per runtime, module scope intact, evaluated on first use — so the exports are
    /// fetched inside the same trip onto the thread that uses them. Handing a
    /// <see cref="JSReference"/> back instead would mean a strong reference per call, kept alive
    /// against a module the runtime is already keeping alive, and nothing to dispose it.
    ///
    /// An ES module whose graph awaits at its top level is one <c>require</c> refuses, as it cannot
    /// be evaluated synchronously. That one is imported, and the work is given its namespace once
    /// the import settles; Node's module loader keeps it, one instance per runtime, as
    /// <c>require.cache</c> keeps the rest. The engine remembers which modules those are, so that
    /// each is refused once.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="work"></param>
    /// <param name="cancellationToken"></param>
    public async Task<T> RunAsync<T>(NodeModuleSource source, Func<JSValue, Task<T>> work, CancellationToken cancellationToken)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (work is null)
            throw new ArgumentNullException(nameof(work));
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        var path = await ResolveAsync(source, cancellationToken);
        return await RunAsync(() => LoadAsync(path, work));
    }

    /// <summary>
    /// Runs synchronous work against a module's exports on this engine's thread.
    /// </summary>
    /// <remarks>
    /// Loading is synchronous here, so an ES module that awaits at its top level fails with Node's
    /// <c>ERR_REQUIRE_ASYNC_MODULE</c>: it can only be had asynchronously.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="work"></param>
    public T Run<T>(NodeModuleSource source, Func<JSValue, T> work)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (work is null)
            throw new ArgumentNullException(nameof(work));
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        var path = ResolveAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        return Run(() => work(Require(path)));
    }

    /// <summary>
    /// Loads a module on this engine ahead of use, so that evaluating it does not land under the
    /// first call that needs it.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="cancellationToken"></param>
    public async Task ImportAsync(NodeModuleSource source, CancellationToken cancellationToken)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        var path = await ResolveAsync(source, cancellationToken);
        await RunAsync(() => LoadAsync(path, exports => Task.FromResult(exports.IsObject())));
    }

    /// <summary>
    /// Resolves a module to its path, logging what is about to be required.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="cancellationToken"></param>
    async Task<string> ResolveAsync(NodeModuleSource source, CancellationToken cancellationToken)
    {
        var path = await source.ResolveAsync(cancellationToken);

        // Once per call, so at the level nobody turns on for anything but this: Debug is for what the pool decides.
        logger.LogTrace("Requiring module {Module} from {Path}.", source.Name, path);

        return path;
    }

    /// <summary>
    /// Node's own loader. Must be called on the engine's thread.
    /// </summary>
    /// <param name="path"></param>
    static JSValue Require(string path) => JSValue.Global["require"].Call(JSValue.Undefined, path);

    /// <summary>
    /// Loads a module, by <c>require</c> or, for an ES module that awaits at its top level, by <c>import()</c>, and runs
    /// work against its exports. On the engine's thread.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="path">The module's path.</param>
    /// <param name="work">The work, given the exports, or the namespace of an imported module.</param>
    async Task<T> LoadAsync<T>(string path, Func<JSValue, Task<T>> work)
    {
        if (imported.Contains(path))
            return await ImportModuleAsync(path, work);

        JSValue exports;

        try
        {
            exports = Require(path);
        }
        catch (JSException e) when (IsAsyncModule(e) && CanImport())
        {
            logger.LogDebug("Importing module {Path}: it awaits at its top level, so require cannot load it.", path);
            imported.Add(path);
            return await ImportModuleAsync(path, work);
        }

        return await work(exports);
    }

    /// <summary>
    /// Imports a module with <c>import()</c>, and runs work against its namespace once it settles. On the engine's
    /// thread.
    /// </summary>
    /// <remarks>
    /// Through the <c>import</c> function node-api-dotnet gives the runtime, which is Node's own <c>import()</c> called
    /// from a CommonJS module on disk, <c>import.cjs</c> beside its assembly: a script run as text has no module to
    /// resolve from, and Node refuses it a dynamic import.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="path">The module's path.</param>
    /// <param name="work">The work.</param>
    static async Task<T> ImportModuleAsync<T>(string path, Func<JSValue, Task<T>> work)
    {
        var namespaceObject = await ((JSPromise)JSValue.Global["import"].Call(JSValue.Undefined, path)).AsTask();
        return await work(namespaceObject);
    }

    /// <summary>
    /// Whether <c>require</c> refused a module as an ES module that awaits at its top level. On the engine's thread.
    /// </summary>
    /// <param name="e">What <c>require</c> threw.</param>
    static bool IsAsyncModule(JSException e)
    {
        try
        {
            return e.Error is { } error && error.Value.IsObject() && (string)error.Value["code"] == "ERR_REQUIRE_ASYNC_MODULE";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the runtime has the <c>import</c> function <see cref="ImportModuleAsync"/> calls: node-api-dotnet gives it
    /// one where it finds <c>import.cjs</c>, which its package deploys beside its assembly. On the engine's thread.
    /// </summary>
    static bool CanImport() => JSValue.Global["import"].IsFunction();

    /// <summary>
    /// Posts work to the engine's thread, quietly dropping it if the engine is gone.
    /// </summary>
    /// <remarks>
    /// The posted delegate swallows everything, deliberately. A queued callback that throws while
    /// the runtime is being deleted recurses inside the native callback's exception dispatch and
    /// takes the process down with a stack overflow — and the only work posted this way is cleanup,
    /// whose failure means the engine is already tearing the world down anyway.
    /// </remarks>
    /// <param name="action"></param>
    public void TryPost(Action action)
    {
        if (disposed)
            return;

        try
        {
            runtime.Post(() =>
            {
                try
                {
                    action();
                }
                catch
                {

                }
            }, allowSync: false);
        }
        catch (ObjectDisposedException)
        {
            // A disposed engine has torn the whole world down already; there is nothing left the
            // posted work could have affected.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (disposed)
            return default;

        disposed = true;
        Release();
        runtime.Dispose();
        return default;
    }

    /// <summary>
    /// Lets go of anything still holding the event loop open, so the runtime can close now.
    /// </summary>
    /// <remarks>
    /// Closing a runtime drains its event loop first, and waits several minutes before giving up on
    /// one that will not drain. Applications leave timers behind as a matter of course — a cache
    /// scheduling its own expiry, a framework releasing a held resource after a grace period — and
    /// each of those is written for a browser or a long-lived server, where firing minutes later is
    /// exactly right. Here the work they would do has no observer: the responses are long since
    /// served and the engine is being taken away. Unreferencing leaves them scheduled but stops them
    /// counting towards the loop having work to do, which is the difference between closing at once
    /// and closing after a wait nobody benefits from.
    ///
    /// Best-effort by nature. It runs on the way out, so anything it fails at is no worse than not
    /// having tried, and the close proceeds regardless.
    /// </remarks>
    void Release()
    {
        try
        {
            runtime.Run(() => JSValue.RunScript(ReleaseScript));
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not release the engine's pending work before closing it.");
        }
    }

}
