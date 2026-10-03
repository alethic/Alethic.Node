using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Web;

using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.AspNet;

/// <summary>
/// Node work done on behalf of one ASP.NET request, with the request's own thread serving whatever the work asks of
/// the site.
/// </summary>
/// <remarks>
/// The work runs on an engine's thread, which is not the request's: it has no <see cref="HttpContext"/>, and code
/// that reads <see cref="HttpContext.Current"/>, as a page's and a handler's does, fails there. So what the work needs
/// done as the request — a handler run for a <c>fetch</c> of the site, an event raised on a page —
/// it hands back with <see cref="InvokeAsync{T}(Func{Task{T}})"/>, and the request's thread, which waits for the work by
/// serving what it hands back, does it. <see cref="RunAsync{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/>
/// serves while it awaits, on the request's synchronization context, so each invocation may itself await;
/// <see cref="Run{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/> blocks the
/// request's thread and serves each to its end, for a page that is not asynchronous.
///
/// A <c>fetch</c> of the site, made by JavaScript the work called through <see cref="Call"/>, is answered in process by
/// the handler the site maps the path to, through <see cref="InProcessRequest"/>, as the visitor this request is for:
/// a URL relative to the site, or absolute on the request's own origin. Anything else is a real request. Which request
/// a <c>fetch</c> belongs to is kept in an <c>AsyncLocalStorage</c>, so requests sharing an engine do not see each
/// other's: module scope is shared by everything an engine runs.
///
/// The engine has to have been prepared for this, by <see cref="InstallAsync"/> as its pool's
/// <see cref="NodeEnginePoolOptions.ConfigureEngine"/>; <see cref="AspNetNode.Pool"/> is.
/// </remarks>
public sealed class NodeRequest
{

    /// <summary>
    /// The name of the global the engine's half of this lives under.
    /// </summary>
    const string BridgeName = "__alethicNodeAspNet";

    /// <summary>
    /// Keeps the request each call belongs to, and answers a <c>fetch</c> of the site through the request's thread.
    /// Completes with the bridge, whose <c>dispatch</c> .NET supplies.
    /// </summary>
    const string InstallScript = """
        (() => {
            const { AsyncLocalStorage } = require('node:async_hooks');
            const requests = new AsyncLocalStorage();
            const fetchOriginal = globalThis.fetch;

            // A response to these statuses has no body, and the Response constructor refuses one.
            const nullBodyStatus = new Set([101, 204, 205, 304]);

            const bridge = {
                run(request, fn, thisArg, args) {
                    return requests.run(request, () => fn.apply(thisArg, args));
                },
                current() {
                    return requests.getStore();
                },
                dispatch: null,
            };

            globalThis.fetch = async function (input, init) {
                const request = requests.getStore();
                if (request === undefined) {
                    return fetchOriginal(input, init);
                }

                const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
                const url = new URL(href, request.origin);
                if (url.origin !== request.origin) {
                    return fetchOriginal(input, init);
                }

                const method = String(init?.method ?? (typeof input === 'object' && input !== null && 'method' in input ? input.method : 'GET')).toUpperCase();
                if (method !== 'GET' && method !== 'HEAD') {
                    throw new TypeError(`Only GET and HEAD of the site are answered in process, not ${method} ${url.pathname}.`);
                }

                const answer = await bridge.dispatch(request.id, url.pathname + url.search);
                const empty = method === 'HEAD' || nullBodyStatus.has(answer.status) || answer.body === '';
                return new Response(empty ? null : answer.body, {
                    status: answer.status,
                    headers: answer.contentType ? { 'content-type': answer.contentType } : {},
                });
            };

            return bridge;
        })();
        """;

    /// <summary>
    /// The requests whose work is under way, by the id the engine knows each by.
    /// </summary>
    static readonly ConcurrentDictionary<string, NodeRequest> active = new();

    readonly object sync = new();

    /// <summary>
    /// What the work under way has handed back to be done, and its id; <see langword="null"/> between runs.
    /// </summary>
    Channel<Invocation>? queue;
    string? id;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="context">The request the work is done for.</param>
    public NodeRequest(HttpContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Origin = context.Request.Url.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The request the work is done for.
    /// </summary>
    public HttpContext Context { get; }

    /// <summary>
    /// The request's origin: a <c>fetch</c> of it is answered in process.
    /// </summary>
    public string Origin { get; }

    /// <summary>
    /// Finds the handler for a path relative to the application's root, for a <c>fetch</c> of the site; the site's
    /// <see cref="HandlerMap"/> where <see langword="null"/>.
    /// </summary>
    public Func<HttpContext, string, IHttpHandler?>? Handlers { get; set; }

    /// <summary>
    /// Prepares an engine for requests: the <c>AsyncLocalStorage</c> that tells them apart, and the <c>fetch</c> that
    /// answers a request for the site in process.
    /// </summary>
    /// <remarks>
    /// For a pool's <see cref="NodeEnginePoolOptions.ConfigureEngine"/>. It replaces the engine's global <c>fetch</c>
    /// with one that hands any request made outside <see cref="Call"/> to the original.
    /// </remarks>
    /// <param name="lease">A lease on the engine.</param>
    public static Task InstallAsync(NodeEngineLease lease)
    {
        if (lease is null)
            throw new ArgumentNullException(nameof(lease));

        return lease.RunAsync(() =>
        {
            var bridge = JSValue.RunScript(InstallScript);
            bridge["dispatch"] = JSValue.CreateFunction("dispatch", Dispatch);
            var global = JSValue.Global;
            global[BridgeName] = bridge;
            return Task.FromResult(true);
        });
    }

    /// <summary>
    /// Runs work against a module on an engine of the application's pool, serving what it asks of the request until it
    /// is done.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    /// <param name="cancellationToken">Stops waiting for the work; it goes on, but nothing serves it.</param>
    public Task<T> RunAsync<T>(NodeModuleSource module, Func<JSValue, Task<T>> work, CancellationToken cancellationToken = default)
    {
        return RunAsync(AspNetNode.Pool, module, work, cancellationToken);
    }

    /// <summary>
    /// Runs work against a module on an engine of a pool, serving what it asks of the request until it is done.
    /// </summary>
    /// <remarks>
    /// Each invocation the work hands back is started as it comes, on the context this was awaited on, and they run
    /// alongside one another. One the work did not wait for is finished before this is.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="pool">The pool, whose engines <see cref="InstallAsync"/> prepared.</param>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    /// <param name="cancellationToken">Stops waiting for the work; it goes on, but nothing serves it.</param>
    public async Task<T> RunAsync<T>(NodeEnginePool pool, NodeModuleSource module, Func<JSValue, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (pool is null)
            throw new ArgumentNullException(nameof(pool));
        if (module is null)
            throw new ArgumentNullException(nameof(module));
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        using var lease = await pool.AcquireAsync(cancellationToken);
        var (queue, running) = Start(lease, module, work);

        try
        {
            var started = new List<Task>();
            while (await queue.Reader.WaitToReadAsync(cancellationToken))
                while (queue.Reader.TryRead(out var invocation))
                    started.Add(invocation.Run());

            await Task.WhenAll(started);
            return await running;
        }
        finally
        {
            Stop(queue);
        }
    }

    /// <summary>
    /// Runs work against a module on an engine of the application's pool, blocking the request's thread, which serves
    /// what the work asks of the request until it is done.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    /// <param name="cancellationToken">Stops waiting for the work; it goes on, but nothing serves it.</param>
    public T Run<T>(NodeModuleSource module, Func<JSValue, Task<T>> work, CancellationToken cancellationToken = default)
    {
        return Run(AspNetNode.Pool, module, work, cancellationToken);
    }

    /// <summary>
    /// Runs work against a module on an engine of a pool, blocking the request's thread, which serves what the work
    /// asks of the request until it is done.
    /// </summary>
    /// <remarks>
    /// For a request that is not asynchronous, such as a page without <c>Async="true"</c>, whose thread holds its
    /// synchronization context until it is done: nothing here waits on that context. Each invocation the work hands back
    /// is done to its end, one at a time, and must finish before it returns; one that answers asynchronously throws,
    /// since waiting for it could wait on the context this thread holds, and belongs to
    /// <see cref="RunAsync{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/>.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="pool">The pool, whose engines <see cref="InstallAsync"/> prepared.</param>
    /// <param name="module">The module.</param>
    /// <param name="work">The work, on the engine's thread, given the module's exports.</param>
    /// <param name="cancellationToken">Stops waiting for the work; it goes on, but nothing serves it.</param>
    /// <exception cref="InvalidOperationException">An invocation answered asynchronously.</exception>
    public T Run<T>(NodeEnginePool pool, NodeModuleSource module, Func<JSValue, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (pool is null)
            throw new ArgumentNullException(nameof(pool));
        if (module is null)
            throw new ArgumentNullException(nameof(module));
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        // Off this thread's context: the pool awaits as it grows, and its continuations must not queue behind a thread
        // that is blocked waiting for them.
        using var lease = Task.Run(() => pool.AcquireAsync(cancellationToken)).GetAwaiter().GetResult();
        var (queue, running) = Start(lease, module, work);

        try
        {
            while (true)
            {
                if (queue.Reader.TryRead(out var invocation))
                {
                    var done = invocation.Run();
                    if (done.IsCompleted == false)
                        throw new InvalidOperationException($"Node work for {Context.Request.Path} handed back something that answers asynchronously, which a request that is not asynchronous cannot wait for.");

                    done.GetAwaiter().GetResult();
                    continue;
                }

                if (queue.Reader.Completion.IsCompleted)
                    break;

                // Waited for without this thread's context, which it holds.
                queue.Reader.WaitToReadAsync().AsTask().Wait(cancellationToken);
            }

            return running.GetAwaiter().GetResult();
        }
        finally
        {
            Stop(queue);
        }
    }

    /// <summary>
    /// Calls a JavaScript function as this request: what it starts — a <c>fetch</c> above all, however much later — knows
    /// the request it belongs to.
    /// </summary>
    /// <remarks>
    /// On the engine's thread, inside the work.
    /// </remarks>
    /// <param name="function">The function.</param>
    /// <param name="thisArg">Its <c>this</c>.</param>
    /// <param name="args">Its arguments.</param>
    /// <returns>What it returned.</returns>
    public JSValue Call(JSValue function, JSValue thisArg, params JSValue[] args)
    {
        if (args is null)
            throw new ArgumentNullException(nameof(args));

        var current = id ?? throw new InvalidOperationException("No Node work is under way for this request: call within the work.");
        var bridge = JSValue.Global[BridgeName];
        if (bridge.IsObject() == false)
            throw new InvalidOperationException($"The engine was not prepared for requests: give its pool {nameof(NodeRequest)}.{nameof(InstallAsync)} as its {nameof(NodeEnginePoolOptions.ConfigureEngine)}, as {nameof(AspNetNode)}'s pool does.");

        var request = JSValue.CreateObject();
        request["id"] = current;
        request["origin"] = Origin;

        var array = JSValue.CreateArray(args.Length);
        for (var i = 0; i < args.Length; i++)
            array[i] = args[i];

        return bridge.CallMethod("run", request, function, thisArg, array);
    }

    /// <summary>
    /// Hands work to the request's thread, from the engine's, and completes with what it produced.
    /// </summary>
    /// <remarks>
    /// Awaited on the engine's thread, it resumes there, where JavaScript values are legal.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work, done as the request.</param>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        return InvokeAsync(() => Task.FromResult(work()));
    }

    /// <summary>
    /// Hands work to the request's thread, from the engine's, and completes with what it produced.
    /// </summary>
    /// <remarks>
    /// Awaited on the engine's thread, it resumes there, where JavaScript values are legal. Work that answers
    /// asynchronously can be served only by <see cref="RunAsync{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/>.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work, done as the request.</param>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = new Invocation(
            async () =>
            {
                try
                {
                    done.TrySetResult(await work());
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
            },
            e => done.TrySetException(e));

        if (queue is not { } current || current.Writer.TryWrite(invocation) == false)
            done.TrySetException(Over());

        return done.Task;
    }

    /// <summary>
    /// Begins a run: registers the request with the engine and starts the work, off the request's context.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="lease">The lease the work runs on.</param>
    /// <param name="module">The module.</param>
    /// <param name="work">The work.</param>
    (Channel<Invocation> Queue, Task<T> Running) Start<T>(NodeEngineLease lease, NodeModuleSource module, Func<JSValue, Task<T>> work)
    {
        var started = Channel.CreateUnbounded<Invocation>(new UnboundedChannelOptions() { SingleReader = true });

        lock (sync)
        {
            if (queue is not null)
                throw new InvalidOperationException("Node work is already under way for this request.");

            queue = started;
            id = Guid.NewGuid().ToString("N");
            active[id] = this;
        }

        var running = Task.Run(() => lease.RunAsync(module, work));
        _ = running.ContinueWith(_ => started.Writer.TryComplete(), TaskScheduler.Default);
        return (started, running);
    }

    /// <summary>
    /// Ends a run: nothing more is served, and whatever was handed back and not served fails.
    /// </summary>
    /// <param name="stopped">The run's queue.</param>
    void Stop(Channel<Invocation> stopped)
    {
        lock (sync)
        {
            if (id is not null)
                active.TryRemove(id, out _);

            queue = null;
            id = null;
        }

        stopped.Writer.TryComplete();
        while (stopped.Reader.TryRead(out var invocation))
            invocation.Fail(Over());
    }

    /// <summary>
    /// What is thrown at work handed back once nothing serves it.
    /// </summary>
    static InvalidOperationException Over()
    {
        return new InvalidOperationException("The request's Node work is over: nothing serves it any more.");
    }

    /// <summary>
    /// Answers the engine's <c>fetch</c> of the site: hands it to the request's thread, and settles the promise it
    /// returns with what the site answered.
    /// </summary>
    /// <param name="args">The request's id, and the path and query asked for.</param>
    static JSValue Dispatch(JSCallbackArgs args)
    {
        var requestId = (string)args[0];
        var pathAndQuery = (string)args[1];
        var promise = JSValue.CreatePromise(out var deferred);

        var answered = active.TryGetValue(requestId, out var request)
            ? request.InvokeAsync(() => InProcessRequest.Get(request.Context, pathAndQuery, request.Handlers))
            : Task.FromException<InProcessResponse>(Over());

        _ = SettleAsync(deferred, answered);
        return promise;
    }

    /// <summary>
    /// Settles a <c>fetch</c>'s promise with the site's answer. Started on the engine's thread, so it resumes there.
    /// </summary>
    /// <param name="deferred">The promise's settling side.</param>
    /// <param name="answered">The site's answer.</param>
    static async Task SettleAsync(JSPromise.Deferred deferred, Task<InProcessResponse> answered)
    {
        try
        {
            var answer = await answered;
            var value = JSValue.CreateObject();
            value["status"] = answer.StatusCode;
            value["contentType"] = answer.ContentType is null ? JSValue.Null : (JSValue)answer.ContentType;
            value["body"] = answer.Body;
            deferred.Resolve(value);
        }
        catch (Exception e)
        {
            // With .NET's stack, which is where it failed.
            var error = JSValue.Global["Error"].CallAsConstructor(e.Message);
            error["stack"] = e.ToString();
            deferred.Reject(new JSError(error));
        }
    }

    /// <summary>
    /// Something the work handed back to the request.
    /// </summary>
    /// <param name="run">Does it, as the request; never throws.</param>
    /// <param name="fail">Fails it, where it will not be done.</param>
    sealed class Invocation(Func<Task> run, Action<Exception> fail)
    {

        /// <summary>
        /// Does it, as the request; never throws.
        /// </summary>
        public Func<Task> Run { get; } = run;

        /// <summary>
        /// Fails it, where it will not be done.
        /// </summary>
        public Action<Exception> Fail { get; } = fail;

    }

}
