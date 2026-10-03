using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.AspNet;

/// <summary>
/// Answers a request with a whole page from the application's <c>fetch</c> handler, on a Node engine.
/// </summary>
/// <remarks>
/// The handler is called as <c>fetch(request, env, ctx)</c>: the application's module exports it as <c>fetch</c> on its
/// default export, or its default export is the function itself. The request is the runtime's own <c>Request</c>, and
/// what the handler answers, its <c>Response</c>, is written back as the site's response: status, headers and body.
///
/// It runs as a <see cref="NodeRequest"/>, so a <c>fetch</c> of the site the application makes while it renders is
/// answered in process by the site's own handlers, as the visitor. Each chunk of a streamed body is written by the
/// request's own thread, which waits for the render by serving those writes.
///
/// The request body is read whole before the application is called: ASP.NET reads it ahead of the handler as it is.
///
/// Mounted through ASP.NET routing with <see cref="NodeRouteCollectionExtensions.MapNode(System.Web.Routing.RouteCollection,
/// string, IHttpHandler)"/>, or mapped as any handler is.
/// </remarks>
public class FetchRequestHandler : HttpTaskAsyncHandler
{

    /// <summary>
    /// The execution context handed to the handler as its third argument.
    /// </summary>
    /// <remarks>
    /// A pooled engine keeps running after the response, so a promise given to <c>waitUntil</c> proceeds whether or not
    /// anything registers it; all this does is see that its rejection does not go unobserved.
    /// <c>passThroughOnException</c> does nothing: there is no origin behind this to pass a request to.
    /// </remarks>
    const string ContextScript = """
        ({
            waitUntil(promise) {
                Promise.resolve(promise).catch(e =>
                    console.error('[Alethic.Node.AspNet] waitUntil rejected:', e));
            },
            passThroughOnException() { },
        })
        """;

    /// <summary>
    /// Headers saying where the application is mounted, which this writes from the request ASP.NET resolved rather
    /// than passing on whatever arrived under those names.
    /// </summary>
    static readonly string[] MountHeaders = ["X-Forwarded-Proto", "X-Forwarded-Host", "X-Forwarded-Prefix"];

    /// <summary>
    /// Response headers the server frames itself, whatever the application says about them.
    /// </summary>
    static readonly string[] FramingHeaders = ["Content-Length", "Transfer-Encoding"];

    readonly NodeEnginePool? _pool;
    readonly Uri _baseUri;
    readonly BodyMode _responseBody;
    readonly Dictionary<string, string> _environment;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="module">The application's server module: a self-contained CommonJS bundle.</param>
    /// <param name="options">How it is served; the defaults where <see langword="null"/>.</param>
    public FetchRequestHandler(NodeModuleSource module, FetchRequestHandlerOptions? options = null)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        options ??= new FetchRequestHandlerOptions();

        _pool = options.Pool;
        _baseUri = options.BaseUri ?? throw new ArgumentException("A handler needs a base address.", nameof(options));
        _responseBody = options.ResponseBody;
        _environment = new Dictionary<string, string>(options.Environment);
    }

    /// <summary>
    /// The application's server module.
    /// </summary>
    public NodeModuleSource Module { get; }

    /// <summary>
    /// Whether one instance can serve several requests: it can, holding nothing of any one of them.
    /// </summary>
    public override bool IsReusable => true;

    /// <summary>
    /// Answers the request with the application's response.
    /// </summary>
    /// <param name="context">The request.</param>
    public override Task ProcessRequestAsync(HttpContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        return ProcessRequestAsync(context, new HttpResponseWrapper(context.Response));
    }

    /// <summary>
    /// Answers the request with the application's response, written to the response given.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="response">Where the response is written.</param>
    internal async Task ProcessRequestAsync(HttpContext context, HttpResponseBase response)
    {
        var url = Url(context.Request);
        var method = context.Request.HttpMethod;
        var headers = CollectHeaders(context.Request);
        var body = await ReadBodyAsync(context.Request);
        var disconnected = ClientDisconnected(response);

        // Buffered, the body goes here and the head waits beside it; streamed, both are written as they come.
        var buffer = _responseBody == BodyMode.Buffered ? new MemoryStream() : null;
        ResponseHead? head = null;

        if (buffer is null)
            response.BufferOutput = false;

        var request = new NodeRequest(context);
        await request.RunAsync(_pool ?? AspNetNode.Pool, Module, async exports =>
        {
            // A bare function as the default export is the fetch handler itself, as createRequestHandler-style
            // factories produce.
            var app = NodeModuleExports.Default(exports);
            var fetch = app.IsFunction() || app.IsNullOrUndefined() ? app : app["fetch"];
            if (fetch.IsFunction() == false)
                throw new InvalidOperationException($"Module '{Module.Name}' has no default export with a fetch function.");

            var controller = JSValue.RunScript("new AbortController()");
            using var controllerReference = new JSReference(controller, isWeak: false);

            // Called as the request, so that a fetch of the site it makes is answered in process.
            var pending = request.Call(
                fetch,
                app.IsFunction() ? JSValue.Undefined : app,
                BuildRequest(url, method, headers, body, controller["signal"]),
                BuildEnvironment(),
                JSValue.RunScript(ContextScript));

            // Values made above are out of scope after an await; only the references are held across one.
            var answer = await ((JSPromise)JSValue.Global["Promise"].CallMethod("resolve", pending)).AsTask();
            var answered = new ResponseHead((int)answer["status"], (string)answer["statusText"], CollectResponseHeaders(answer));

            var stream = answer["body"];
            using var reader = stream.IsNullOrUndefined() ? null : new JSReference(stream.CallMethod("getReader"), isWeak: false);

            if (buffer is null)
                await request.InvokeAsync(() =>
                {
                    WriteHead(response, answered);
                    response.Flush();
                    return true;
                });
            else
                head = answered;

            if (reader is null)
                return true;

            while (true)
            {
                // The visitor has gone: the render is told, and stops at its next chance.
                if (disconnected.IsCancellationRequested)
                {
                    controllerReference.GetValue().CallMethod("abort", "the request was aborted");
                    reader.GetValue().CallMethod("cancel").CallMethod("catch", JSValue.CreateFunction("ignore", _ => JSValue.Undefined));
                    break;
                }

                var chunk = await ((JSPromise)reader.GetValue().CallMethod("read")).AsTask();
                if ((bool)chunk["done"])
                    break;

                // Copied into .NET memory while still inside the scope that produced it.
                var bytes = ((JSTypedArray<byte>)chunk["value"]).Span.ToArray();

                if (buffer is not null)
                    buffer.Write(bytes, 0, bytes.Length);
                else
                    await request.InvokeAsync(() =>
                    {
                        response.OutputStream.Write(bytes, 0, bytes.Length);
                        response.Flush();
                        return true;
                    });
            }

            return true;
        });

        if (buffer is not null && head is not null)
        {
            WriteHead(response, head);
            buffer.Position = 0;
            await buffer.CopyToAsync(response.OutputStream);
        }
    }

    /// <summary>
    /// The URL the application is asked at: the path below the site's root, under the base address.
    /// </summary>
    /// <param name="request">The request.</param>
    string Url(HttpRequest request)
    {
        var path = request.Url.AbsolutePath;
        var prefix = Prefix(request);
        if (prefix.Length > 0 && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (path.Length == prefix.Length || path[prefix.Length] == '/'))
            path = path.Length == prefix.Length ? "/" : path.Substring(prefix.Length);

        // Concatenated rather than resolved: the path is rooted, and resolving it would drop the base address's own.
        return string.Concat(
            _baseUri.GetLeftPart(UriPartial.Authority),
            _baseUri.AbsolutePath.TrimEnd('/'),
            path,
            request.Url.Query);
    }

    /// <summary>
    /// Where the site is mounted: its virtual path, or empty at the root.
    /// </summary>
    /// <param name="request">The request.</param>
    static string Prefix(HttpRequest request)
    {
        return (request.ApplicationPath ?? "/").TrimEnd('/');
    }

    /// <summary>
    /// The request's headers, flattened, with where the application is mounted stated afresh.
    /// </summary>
    /// <remarks>
    /// <c>Host</c> is dropped, the authority the application is asked at being in the URL. The mount headers are
    /// dropped and written again, so a visitor cannot describe the mount and have it read as the site's word.
    /// </remarks>
    /// <param name="request">The request.</param>
    static List<KeyValuePair<string, string>> CollectHeaders(HttpRequest request)
    {
        var headers = new List<KeyValuePair<string, string>>();

        foreach (var name in request.Headers.AllKeys)
        {
            if (name is null || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (MountHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var value in request.Headers.GetValues(name) ?? [])
                headers.Add(new(name, value));
        }

        headers.Add(new("X-Forwarded-Proto", request.Url.Scheme));
        headers.Add(new("X-Forwarded-Host", request.Headers["Host"] ?? request.Url.Authority));

        // Absent at the root, as a proxy rewriting no prefix sends it.
        var prefix = Prefix(request);
        if (prefix.Length > 0)
            headers.Add(new("X-Forwarded-Prefix", prefix));

        return headers;
    }

    /// <summary>
    /// The request's body, where its method has one.
    /// </summary>
    /// <param name="request">The request.</param>
    static async Task<byte[]?> ReadBodyAsync(HttpRequest request)
    {
        if (request.HttpMethod is "GET" or "HEAD")
            return null;

        using var body = new MemoryStream();
        await request.InputStream.CopyToAsync(body);
        return body.Length > 0 ? body.ToArray() : null;
    }

    /// <summary>
    /// Cancelled when the visitor goes; never, where the server cannot tell.
    /// </summary>
    /// <param name="response">The response.</param>
    static CancellationToken ClientDisconnected(HttpResponseBase response)
    {
        try
        {
            return response.ClientDisconnectedToken;
        }
        catch (PlatformNotSupportedException)
        {
            return CancellationToken.None;
        }
    }

    /// <summary>
    /// Builds the environment object the handler receives. On the engine's thread.
    /// </summary>
    JSValue BuildEnvironment()
    {
        var env = JSValue.CreateObject();
        foreach (var pair in _environment)
            env[pair.Key] = pair.Value;

        return env;
    }

    /// <summary>
    /// Builds the runtime's own <c>Request</c>. On the engine's thread.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="method">The method.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="body">The body, where there is one.</param>
    /// <param name="signal">Aborts it.</param>
    static JSValue BuildRequest(string url, string method, List<KeyValuePair<string, string>> headers, byte[]? body, JSValue signal)
    {
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

        if (body is not null)
            init["body"] = new JSTypedArray<byte>(body);

        return JSValue.Global["Request"].CallAsConstructor(url, init);
    }

    /// <summary>
    /// Reads the response's headers out through their own iterator. On the engine's thread.
    /// </summary>
    /// <param name="response">The response.</param>
    static List<KeyValuePair<string, string>> CollectResponseHeaders(JSValue response)
    {
        var headers = new List<KeyValuePair<string, string>>();
        var entries = response["headers"].CallMethod("entries");

        while (true)
        {
            var step = entries.CallMethod("next");
            if ((bool)step["done"])
                break;

            var pair = step["value"];
            headers.Add(new((string)pair[0], (string)pair[1]));
        }

        return headers;
    }

    /// <summary>
    /// Writes the status and headers the application answered with.
    /// </summary>
    /// <remarks>
    /// IIS is told not to put its own error page in place of the application's: a 404 the application rendered is
    /// the page the visitor should see. The framing headers are dropped, the body's length and coding being the
    /// server's to state.
    /// </remarks>
    /// <param name="response">The response.</param>
    /// <param name="head">What the application answered with.</param>
    static void WriteHead(HttpResponseBase response, ResponseHead head)
    {
        response.TrySkipIisCustomErrors = true;
        response.StatusCode = head.Status;
        if (string.IsNullOrEmpty(head.StatusText) == false)
            response.StatusDescription = head.StatusText;

        foreach (var header in head.Headers)
        {
            if (FramingHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                continue;

            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                WriteContentType(response, header.Value);
            else
                response.AppendHeader(header.Key, header.Value);
        }
    }

    /// <summary>
    /// Writes the content type through the response's own properties, which is where ASP.NET writes it from: a charset
    /// in it becomes the response's charset, so that ASP.NET does not add a second.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="value">The content type.</param>
    static void WriteContentType(HttpResponseBase response, string value)
    {
        var parts = value.Split(';');
        var charset = parts.Skip(1)
            .Select(i => i.Trim())
            .FirstOrDefault(i => i.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));

        if (charset is null)
        {
            response.ContentType = value;
            return;
        }

        response.ContentType = string.Join(";", parts.Where(i => i.Trim().StartsWith("charset=", StringComparison.OrdinalIgnoreCase) == false)).Trim();
        response.Charset = charset.Substring("charset=".Length).Trim('"');
    }

    /// <summary>
    /// The part of a response known before its body has arrived.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="statusText">The status's text.</param>
    /// <param name="headers">The headers.</param>
    sealed class ResponseHead(int status, string statusText, List<KeyValuePair<string, string>> headers)
    {

        /// <summary>
        /// The status.
        /// </summary>
        public int Status { get; } = status;

        /// <summary>
        /// The status's text.
        /// </summary>
        public string StatusText { get; } = statusText;

        /// <summary>
        /// The headers.
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; } = headers;

    }

}
