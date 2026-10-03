using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Node.Http;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.AspNetCore;

/// <summary>
/// Renders by calling the application's <c>fetch</c> handler, on a Node engine pool.
/// </summary>
/// <remarks>
/// The stock <see cref="INodeRequestHandler"/>, and the one most applications need. The handler is called
/// as <c>fetch(request, env, ctx)</c>, in the protocol <see cref="FetchProtocol"/> defines, which
/// Alethic.Node.AspNet's handler speaks as well: the same application is served by either.
///
/// Conformance stops at the handler. The convention's other exports, <c>scheduled</c>, <c>queue</c>
/// and <c>tail</c>, are not called, this being an HTTP handler. Asynchronous per-engine startup is the
/// application's own to memoize in module scope, as it would be on Workers, where module scope is per
/// isolate and here it is per engine.
///
/// The module evaluates untouched: no adapter is appended and nothing is serialized on the way in
/// or out. The <c>Request</c> and <c>Response</c> objects are built and taken apart directly on the
/// engine's thread, where those types live.
///
/// A framework whose server protocol does not lower to a fetch handler gets its own handler
/// instead. Reading routes needs no handler of its own: pair an <see cref="INodeRouteProvider"/>
/// with this one.
///
/// Open to derivation, for the specialization that is a variation on this handler rather than a
/// protocol of its own: a host with something to add to the request, or to say about the response,
/// overrides <see cref="HandleAsync"/> and calls back. A handler speaking some other protocol
/// implements <see cref="INodeRequestHandler"/> directly instead.
/// </remarks>
public class FetchRequestHandler : INodeRequestHandler
{

    readonly NodeEnginePool pool;
    readonly NodeModuleSource module;
    readonly Uri baseUri;
    readonly BodyMode requestBody;
    readonly BodyMode responseBody;
    readonly Dictionary<string, string> environment;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <remarks>
    /// Holds nothing beyond this. The module is Node's to load and cache; the environment is rebuilt
    /// per request from these values, being input rather than a place to keep things.
    ///
    /// The logger is optional so that the ordinary case reads as one <c>new</c>.
    /// </remarks>
    /// <param name="pool">The pool the application runs on.</param>
    /// <param name="options">How it is served.</param>
    /// <param name="logger">Where a failed render is logged.</param>
    public FetchRequestHandler(NodeEnginePool pool, FetchRequestHandlerOptions options, ILogger<FetchRequestHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        this.logger = logger ?? NullLogger<FetchRequestHandler>.Instance;

        module = options.Module ?? throw new ArgumentException("A handler needs a module.", nameof(options));
        baseUri = options.BaseUri ?? throw new ArgumentException("A handler needs a base address.", nameof(options));
        requestBody = options.RequestBody;
        responseBody = options.ResponseBody;
        environment = new Dictionary<string, string>(options.Environment);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads the module on every engine, so that evaluating it, which occupies an engine's event
    /// loop, happens at startup rather than under whichever request arrives first. A module that
    /// cannot be loaded fails here, and with it the deployment.
    /// </remarks>
    public virtual Task PrepareAsync(CancellationToken cancellationToken = default) =>
        pool.PrepareAsync(lease => lease.ImportAsync(module, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public virtual async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.RequestAborted;

        // The path below the mount, under the address the application is asked at, and where the
        // caller was in the forwarded headers: the mount is PathBase, which ASP.NET has already taken
        // off the path.
        var url = FetchProtocol.Url(baseUri, context.Request.Path.ToUriComponent(), context.Request.QueryString.ToUriComponent());
        var method = context.Request.Method;
        var headers = FetchProtocol.Headers(
            Flatten(context.Request.Headers),
            context.Request.Scheme,
            context.Request.Host.HasValue ? context.Request.Host.Value : null,
            context.Request.PathBase.HasValue ? context.Request.PathBase.Value : null);

        // The stream itself, not its contents: the application reads it as it needs it, so an upload
        // is never held in memory here on its way through. Buffered, it is drained first, here,
        // where waiting on a socket costs nothing, rather than on the engine's thread.
        Stream? body = context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count > 0
            ? context.Request.Body
            : null;

        byte[]? buffered = null;
        if (body is not null && requestBody == BodyMode.Buffered)
        {
            using var drained = new MemoryStream();
            await body.CopyToAsync(drained, cancellationToken);
            buffered = drained.ToArray();
        }

        var lease = await pool.AcquireAsync(cancellationToken);
        var pipe = new Pipe();
        var head = new TaskCompletionSource<FetchResponseHead>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pump = null;

        try
        {
            // The render runs ahead of the copy: the head settles as soon as the application answers,
            // and the body arrives behind it. A fault before the head surfaces here; one after it can
            // only truncate the body, the status having already gone out.
            pump = PumpAsync(lease, url, method, headers, buffered, buffered is null ? body : null, pipe.Writer, head, cancellationToken);

            var completed = await Task.WhenAny(head.Task, pump);
            if (completed == pump)
                await pump; // faulted before producing a head; observe the exception

            var headValue = await head.Task;

            if (responseBody == BodyMode.Buffered)
            {
                // Drained as it is produced, not left unread, which would stall the render against
                // the pipe's own backpressure, but written nowhere yet. A fault reaches this as an
                // exception out of the copy, with the response still unstarted and the status still
                // the host's to decide.
                using var output = new MemoryStream();
                await CopyAsync(pipe.Reader, output, cancellationToken);

                WriteHead(context, headValue);
                context.Response.ContentLength = output.Length;

                output.Position = 0;
                await output.CopyToAsync(context.Response.Body, cancellationToken);
            }
            else
            {
                WriteHead(context, headValue);
                await CopyAsync(pipe.Reader, context.Response.Body, cancellationToken);
            }
        }
        finally
        {
            // In this order: completing the reader is what unblocks a pump still writing, so it has
            // to happen before the pump is waited on; the pump's fault, if any, was already delivered
            // through the head or the body and is only observed here; and the lease is released last,
            // once nothing is still running against the engine.
            await pipe.Reader.CompleteAsync();

            if (pump is not null)
            {
                try
                {
                    await pump;
                }
                catch
                {
                }
            }

            await lease.DisposeAsync();
        }
    }

    /// <summary>
    /// The request's headers, each value on its own.
    /// </summary>
    /// <param name="headers">The headers.</param>
    static IEnumerable<KeyValuePair<string, string?>> Flatten(IHeaderDictionary headers)
    {
        foreach (var header in headers)
            foreach (var value in header.Value)
                yield return new(header.Key, value);
    }

    /// <summary>
    /// Writes the status and headers the application answered with, less the framing headers.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="head">What the application answered with.</param>
    static void WriteHead(HttpContext context, FetchResponseHead head)
    {
        context.Response.StatusCode = head.Status;

        foreach (var (name, value) in head.Headers)
        {
            if (FetchProtocol.IsFramingHeader(name))
                continue;

            context.Response.Headers.Append(name, value);
        }
    }

    /// <summary>
    /// Copies the rendered body to the response as it is produced.
    /// </summary>
    /// <remarks>
    /// Flushed per read rather than copied wholesale, so progress the render makes is progress the
    /// client sees, which is the whole point of a shell reaching the browser ahead of the content
    /// suspended behind it.
    /// </remarks>
    /// <param name="reader">The rendered body.</param>
    /// <param name="destination">Where it is copied.</param>
    /// <param name="cancellationToken">Stops the copy.</param>
    static async Task CopyAsync(PipeReader reader, Stream destination, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);

            foreach (var segment in result.Buffer)
            {
                await destination.WriteAsync(segment, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            reader.AdvanceTo(result.Buffer.End);

            if (result.IsCompleted || result.IsCanceled)
                break;
        }
    }

    /// <summary>
    /// Dispatches the request on the lease's engine and drains the response body into the pipe.
    /// </summary>
    /// <remarks>
    /// One trip onto the engine's thread for the whole render. Everything held across an await goes
    /// through a <see cref="JSReference"/>, awaiting being what ends a value's scope; the abort
    /// controller is additionally posted back rather than called from the cancellation thread,
    /// because that is where it lives.
    /// </remarks>
    /// <param name="lease">The lease the render runs on.</param>
    /// <param name="url">The URL the application is asked at.</param>
    /// <param name="method">The method.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="buffered">The request body, drained, where it was.</param>
    /// <param name="streamed">The request body, as a stream, where it is not drained.</param>
    /// <param name="writer">Where the response body goes.</param>
    /// <param name="head">Settled with the response's head.</param>
    /// <param name="cancellationToken">Aborts the request.</param>
    async Task PumpAsync(NodeEngineLease lease, string url, string method, List<KeyValuePair<string, string>> headers, byte[]? buffered, Stream? streamed, PipeWriter writer, TaskCompletionSource<FetchResponseHead> head, CancellationToken cancellationToken)
    {
        try
        {
            await lease.RunAsync(module, async exports =>
            {
                var fetch = FetchProtocol.Handler(exports, module.Name, out var app);

                var controller = FetchProtocol.AbortController();
                using var controllerRef = new JSReference(controller, isWeak: false);
                using var registration = cancellationToken.Register(() => lease.TryPost(
                    () => controllerRef.GetValue().CallMethod("abort", "the request was aborted")));

                JSValue? body = buffered is not null ? (JSValue)new JSTypedArray<byte>(buffered)
                    : streamed is not null ? FetchProtocol.BodyStream(lease, streamed, cancellationToken)
                    : (JSValue?)null;

                var request = FetchProtocol.Request(url, method, headers, controller["signal"], body);

                // Three arguments, as the convention specifies: the request, the host environment, and an
                // execution context.
                using var answer = await FetchProtocol.RespondAsync(() =>
                    fetch.Call(app, request, FetchProtocol.Environment(environment), FetchProtocol.Context()));

                var response = answer.GetValue();
                head.TrySetResult(FetchProtocol.Head(response));

                await FetchProtocol.BodyAsync(response, async bytes =>
                {
                    var flushed = await writer.WriteAsync(bytes, CancellationToken.None);
                    return flushed.IsCompleted == false; // false where the consumer gave up on the body
                });

                return 0;
            }, cancellationToken);

            await writer.CompleteAsync();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Render of module {Module} failed.", module.Name);
            head.TrySetException(e);
            await writer.CompleteAsync(e);
        }
    }

}
