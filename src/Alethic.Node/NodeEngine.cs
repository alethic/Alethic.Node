using System;
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
    /// intact, because that is what <c>require</c> means.
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
        TrackTimersScript;

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
    /// The longest a probe has waited for the engine's thread since the pool last adapted, in milliseconds. Kept under
    /// the pool's lock.
    /// </summary>
    internal double ProbeMax;

    /// <summary>
    /// When the probe under way was posted, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp; zero while none is.
    /// Kept under the pool's lock.
    /// </summary>
    internal long ProbeStarted;

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
    /// Posts nothing to the engine's thread, and reports when it started there, as a
    /// <see cref="System.Diagnostics.Stopwatch"/> timestamp.
    /// </summary>
    /// <remarks>
    /// The time from posting to starting is the engine's event-loop delay, as work given to it sees it: what is posted
    /// waits behind whatever the thread is already busy with, and only that. Taken on the engine's thread, so the trip
    /// back to the caller is not in it.
    /// </remarks>
    internal Task<long> ProbeAsync()
    {
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);

        return runtime.RunAsync(() => Task.FromResult(System.Diagnostics.Stopwatch.GetTimestamp()));
    }

    /// <summary>
    /// The underlying runtime.
    /// </summary>
    internal NodeEmbeddingThreadRuntime Runtime => runtime;

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
        return await runtime.RunAsync(() => work(Require(path)));
    }

    /// <summary>
    /// Runs synchronous work against a module's exports on this engine's thread.
    /// </summary>
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
        return runtime.Run(() => work(Require(path)));
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
        runtime.Run(() => Require(path).IsObject());
    }

    /// <summary>
    /// Resolves a module to its path, logging what is about to be required.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="cancellationToken"></param>
    async Task<string> ResolveAsync(NodeModuleSource source, CancellationToken cancellationToken)
    {
        var path = await source.ResolveAsync(cancellationToken);
        logger.LogDebug("Requiring module {Module} from {Path}.", source.Name, path);

        return path;
    }

    /// <summary>
    /// Node's own loader. Must be called on the engine's thread.
    /// </summary>
    /// <param name="path"></param>
    static JSValue Require(string path) => JSValue.Global["require"].Call(JSValue.Undefined, path);

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
