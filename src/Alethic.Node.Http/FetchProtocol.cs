using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.Http;

/// <summary>
/// The protocol an application's <c>fetch</c> handler is served by: how a request is put to it, and how its answer is
/// read, the same whatever host serves it.
/// </summary>
/// <remarks>
/// The handler is called as <c>fetch(request, env, ctx)</c>, the shape Cloudflare Workers defines and Deno, Bun and
/// the framework adapters targeting them follow. It is found as <c>fetch</c> on the module's default export, or is the
/// default export itself, which is what <c>createRequestHandler</c>-style factories produce. Nothing is asked of an
/// application beyond it.
///
/// The application is a proxy's origin, addressed the way an origin behind one is: at <see cref="DefaultBaseUri"/>
/// unless the host says otherwise, with the mount removed from the path, and where the caller was told in
/// <c>X-Forwarded-Proto</c>, <c>X-Forwarded-Host</c> and <c>X-Forwarded-Prefix</c>, which the host writes and a caller
/// cannot.
///
/// The members that touch JavaScript run on the engine's thread, inside the host's work; the rest are plain .NET. What
/// is the host's own, reading its request and writing its response, stays with the host.
/// </remarks>
public static class FetchProtocol
{

    /// <summary>
    /// The address an application is asked at unless its host says otherwise. Reserved by RFC 2606 against ever
    /// resolving, so it cannot pass for the address the caller used.
    /// </summary>
    public static readonly Uri DefaultBaseUri = new Uri("http://node.invalid/");

    /// <summary>
    /// The execution context handed to the handler as its third argument.
    /// </summary>
    /// <remarks>
    /// <c>waitUntil</c> exists on Workers because the isolate is frozen once the response is returned, and work not
    /// registered with it is killed. A pooled engine is not frozen, so the promise proceeds whether or not anything
    /// registers it, and all this has to do is see that a rejection does not go unobserved.
    /// <c>passThroughOnException</c> does nothing: there is no origin behind this to pass a request through to.
    /// </remarks>
    const string ContextScript = """
        ({
            waitUntil(promise) {
                Promise.resolve(promise).catch(e =>
                    console.error('[Alethic.Node.Http] waitUntil rejected:', e));
            },
            passThroughOnException() { },
        })
        """;

    /// <summary>
    /// Headers saying where the application is mounted, which the host writes from the request it resolved rather
    /// than passing on whatever arrived under those names.
    /// </summary>
    static readonly string[] MountHeaders = ["X-Forwarded-Proto", "X-Forwarded-Host", "X-Forwarded-Prefix"];

    /// <summary>
    /// Response headers the server frames itself, whatever the application says about them.
    /// </summary>
    static readonly string[] FramingHeaders = ["Content-Length", "Transfer-Encoding"];

    /// <summary>
    /// How much of a streamed request body is asked for at a time.
    /// </summary>
    /// <remarks>
    /// The stream asks again only when the application has taken what it was given, so this bounds what is in flight
    /// rather than what is transferred: a large upload costs this much memory, not its own size.
    /// </remarks>
    const int BodyChunkSize = 16 * 1024;

    /// <summary>
    /// The URL the application is asked at: the path below the mount, under the base address.
    /// </summary>
    /// <remarks>
    /// Concatenated rather than resolved: the path is rooted, and URI resolution treats a rooted reference as
    /// absolute, which would drop any path the base address carries instead of inserting it.
    /// </remarks>
    /// <param name="baseUri">The base address.</param>
    /// <param name="path">The path below the mount, rooted and escaped as it arrived.</param>
    /// <param name="query">The query, with its <c>?</c>, or empty.</param>
    public static string Url(Uri baseUri, string path, string query)
    {
        if (baseUri is null)
            throw new ArgumentNullException(nameof(baseUri));

        return string.Concat(
            baseUri.GetLeftPart(UriPartial.Authority),
            baseUri.AbsolutePath.TrimEnd('/'),
            string.IsNullOrEmpty(path) ? "/" : path,
            query ?? "");
    }

    /// <summary>
    /// The headers the application is asked with: the caller's, less <c>Host</c>, with where the application is
    /// mounted stated afresh.
    /// </summary>
    /// <remarks>
    /// <c>Host</c> is dropped: the authority the application is asked at is in the URL, and a Host header beside it
    /// would contradict it. The mount headers are dropped and written again, so a caller cannot describe the mount to
    /// the application and have it read as though the host had said so.
    /// </remarks>
    /// <param name="incoming">The caller's headers, each value of a repeated header on its own.</param>
    /// <param name="scheme">The scheme the caller used.</param>
    /// <param name="host">The host the caller asked for, where it is known.</param>
    /// <param name="prefix">Where the host mounted the application; absent at the root, as a proxy rewriting no prefix
    /// sends it.</param>
    public static List<KeyValuePair<string, string>> Headers(IEnumerable<KeyValuePair<string, string?>> incoming, string scheme, string? host, string? prefix)
    {
        if (incoming is null)
            throw new ArgumentNullException(nameof(incoming));

        var headers = new List<KeyValuePair<string, string>>();

        foreach (var header in incoming)
        {
            if (header.Value is null || string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (MountHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                continue;

            headers.Add(new KeyValuePair<string, string>(header.Key, header.Value));
        }

        headers.Add(new KeyValuePair<string, string>("X-Forwarded-Proto", scheme));

        if (string.IsNullOrEmpty(host) == false)
            headers.Add(new KeyValuePair<string, string>("X-Forwarded-Host", host!));

        if (string.IsNullOrEmpty(prefix) == false && prefix != "/")
            headers.Add(new KeyValuePair<string, string>("X-Forwarded-Prefix", prefix!));

        return headers;
    }

    /// <summary>
    /// Whether a response header is one the server frames itself, which the host leaves out: the body's length and
    /// coding are the server's to state, and what the application said about them described its own stream.
    /// </summary>
    /// <param name="name">The header's name.</param>
    public static bool IsFramingHeader(string name)
    {
        return FramingHeaders.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the application's <c>fetch</c> handler, and what it is called on. On the engine's thread.
    /// </summary>
    /// <param name="exports">The module's exports.</param>
    /// <param name="moduleName">The module's name, for the message where there is no handler.</param>
    /// <param name="thisArg">What the handler is called on: the default export, or nothing where it is the handler.</param>
    /// <exception cref="InvalidOperationException">The module has no handler.</exception>
    public static JSValue Handler(JSValue exports, string moduleName, out JSValue thisArg)
    {
        var app = NodeModuleExports.Default(exports);
        if (app.IsFunction())
        {
            thisArg = JSValue.Undefined;
            return app;
        }

        var fetch = app.IsNullOrUndefined() ? app : app["fetch"];
        if (fetch.IsFunction() == false)
            throw new InvalidOperationException($"Module '{moduleName}' has no default export with a fetch function.");

        thisArg = app;
        return fetch;
    }

    /// <summary>
    /// The handler's <c>env</c>: the host's values, in an object made for this request. On the engine's thread.
    /// </summary>
    /// <param name="values">The values.</param>
    public static JSValue Environment(IEnumerable<KeyValuePair<string, string>> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));

        var env = JSValue.CreateObject();
        foreach (var pair in values)
            env[pair.Key] = pair.Value;

        return env;
    }

    /// <summary>
    /// The handler's <c>ctx</c>: <c>waitUntil</c> and <c>passThroughOnException</c>. On the engine's thread.
    /// </summary>
    public static JSValue Context()
    {
        return JSValue.RunScript(ContextScript);
    }

    /// <summary>
    /// An <c>AbortController</c>, whose signal the request carries. On the engine's thread.
    /// </summary>
    public static JSValue AbortController()
    {
        return JSValue.Global["AbortController"].CallAsConstructor();
    }

    /// <summary>
    /// The runtime's own <c>Request</c>, built value by value. On the engine's thread.
    /// </summary>
    /// <param name="url">The URL, from <see cref="Url"/>.</param>
    /// <param name="method">The method.</param>
    /// <param name="headers">The headers, from <see cref="Headers"/>.</param>
    /// <param name="signal">The signal that aborts it.</param>
    /// <param name="body">The body, where there is one: a <c>Uint8Array</c>, or a <c>ReadableStream</c> from
    /// <see cref="BodyStream"/>.</param>
    public static JSValue Request(string url, string method, IReadOnlyList<KeyValuePair<string, string>> headers, JSValue signal, JSValue? body = null)
    {
        if (headers is null)
            throw new ArgumentNullException(nameof(headers));

        var init = JSValue.CreateObject();
        init["method"] = method;
        init["signal"] = signal;

        // Headers in fetch's pair form: an array of [name, value] arrays.
        var pairs = JSValue.CreateArray(headers.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            var pair = JSValue.CreateArray(2);
            pair[0] = headers[i].Key;
            pair[1] = headers[i].Value;
            pairs[i] = pair;
        }

        init["headers"] = pairs;

        if (body is JSValue value && value.IsNullOrUndefined() == false)
        {
            init["body"] = value;

            // Required of a request whose body is a stream: it says the body is not being written in response to what
            // comes back, which is the only mode this is.
            if (value.InstanceOf(JSValue.Global["ReadableStream"]))
                init["duplex"] = "half";
        }

        return JSValue.Global["Request"].CallAsConstructor(url, init);
    }

    /// <summary>
    /// Presents a request body to the application as a <c>ReadableStream</c>, read as the application asks for it. On
    /// the engine's thread.
    /// </summary>
    /// <remarks>
    /// A hand-built underlying source: <c>pull</c> is a .NET delegate exposed as a JS function, called on the engine's
    /// thread whenever the application wants more. It answers a promise, so the read itself happens off that thread and
    /// the engine's event loop is never held waiting on a socket; the promise is settled back on the engine's thread,
    /// posted through the lease.
    /// </remarks>
    /// <param name="lease">The lease the work runs on.</param>
    /// <param name="body">The body.</param>
    /// <param name="cancellationToken">Stops reading it.</param>
    public static JSValue BodyStream(NodeEngineLease lease, Stream body, CancellationToken cancellationToken)
    {
        if (lease is null)
            throw new ArgumentNullException(nameof(lease));
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        var source = JSValue.CreateObject();

        source["pull"] = JSValue.CreateFunction("pull", args =>
        {
            var controller = new JSReference(args[0], isWeak: false);
            var promise = JSValue.CreatePromise(out var deferred);
            _ = PullAsync(lease, body, controller, deferred, cancellationToken);
            return promise;
        });

        return JSValue.Global["ReadableStream"].CallAsConstructor(source);
    }

    /// <summary>
    /// Reads one chunk of a request body and hands it to the stream's controller.
    /// </summary>
    /// <remarks>
    /// Everything touching JS is posted, and everything posted swallows: work queued onto an engine that is being torn
    /// down runs during that teardown, and a throw from there recurses inside the native callback rather than
    /// surfacing anywhere useful.
    /// </remarks>
    /// <param name="lease">The lease the work runs on.</param>
    /// <param name="body">The body.</param>
    /// <param name="controller">The stream's controller.</param>
    /// <param name="deferred">Settles the promise <c>pull</c> returned.</param>
    /// <param name="cancellationToken">Stops reading it.</param>
    static async Task PullAsync(NodeEngineLease lease, Stream body, JSReference controller, JSPromise.Deferred deferred, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[BodyChunkSize];
            var read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

            lease.TryPost(() =>
            {
                try
                {
                    if (read == 0)
                        controller.GetValue().CallMethod("close");
                    else
                        controller.GetValue().CallMethod("enqueue", new JSTypedArray<byte>(new Memory<byte>(buffer, 0, read)));

                    deferred.Resolve(JSValue.Undefined);
                }
                catch
                {
                }
                finally
                {
                    controller.Dispose();
                }
            });
        }
        catch (Exception e)
        {
            // The stream errors rather than the promise going unobserved: what pull returns is consumed by the stream
            // machinery, so a rejection here is handled there.
            lease.TryPost(() =>
            {
                try
                {
                    deferred.Reject(new JSError(e.Message));
                }
                catch
                {
                }
                finally
                {
                    controller.Dispose();
                }
            });
        }
    }

    /// <summary>
    /// Calls the handler, and waits for its <c>Response</c>. On the engine's thread.
    /// </summary>
    /// <remarks>
    /// The values handed in are out of scope once this has awaited; what the caller needs after it, it holds through a
    /// <see cref="JSReference"/>. The response is returned as a reference for the same reason.
    /// </remarks>
    /// <param name="call">Calls the handler, as the host must call it, and returns what it returned.</param>
    public static async Task<JSReference> RespondAsync(Func<JSValue> call)
    {
        if (call is null)
            throw new ArgumentNullException(nameof(call));

        var pending = call();
        var response = await ((JSPromise)JSValue.Global["Promise"].CallMethod("resolve", pending)).AsTask();
        return new JSReference(response, isWeak: false);
    }

    /// <summary>
    /// Reads a response's status and headers, the headers through their own iterator. On the engine's thread.
    /// </summary>
    /// <param name="response">The response.</param>
    public static FetchResponseHead Head(JSValue response)
    {
        var headers = new List<KeyValuePair<string, string>>();
        var entries = response["headers"].CallMethod("entries");

        while (true)
        {
            var step = entries.CallMethod("next");
            if ((bool)step["done"])
                break;

            var pair = step["value"];
            headers.Add(new KeyValuePair<string, string>((string)pair[0], (string)pair[1]));
        }

        var statusText = response["statusText"];
        return new FetchResponseHead((int)response["status"], statusText.IsString() ? (string)statusText : "", headers);
    }

    /// <summary>
    /// Reads a response's body, handing each chunk on as it arrives. On the engine's thread.
    /// </summary>
    /// <remarks>
    /// Each chunk is copied into .NET memory while still inside the scope that produced it. Where the host answers that
    /// it wants no more, the body is cancelled, so the application can stop producing it.
    /// </remarks>
    /// <param name="response">The response.</param>
    /// <param name="write">Takes a chunk, awaited before the next is read; answers whether to go on.</param>
    public static async Task BodyAsync(JSValue response, Func<byte[], Task<bool>> write)
    {
        if (write is null)
            throw new ArgumentNullException(nameof(write));

        var stream = response["body"];
        if (stream.IsNullOrUndefined())
            return;

        using var reader = new JSReference(stream.CallMethod("getReader"), isWeak: false);

        while (true)
        {
            var chunk = await ((JSPromise)reader.GetValue().CallMethod("read")).AsTask();
            if ((bool)chunk["done"])
                break;

            var bytes = ((JSTypedArray<byte>)chunk["value"]).Span.ToArray();
            if (await write(bytes) == false)
            {
                // Its rejection is of no interest, but must not go unobserved.
                reader.GetValue().CallMethod("cancel").CallMethod("catch", JSValue.CreateFunction("ignore", _ => JSValue.Undefined));
                break;
            }
        }
    }

}
