using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Alethic.Node.Http;

using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.AspNet;

/// <summary>
/// Answers a request with a whole page from the application's <c>fetch</c> handler, on a Node engine.
/// </summary>
/// <remarks>
/// The handler is called as <c>fetch(request, env, ctx)</c>, in the protocol <see cref="FetchProtocol"/> defines, which
/// Alethic.Node.AspNetCore's handler speaks as well: the same application is served by either. What the handler
/// answers, its <c>Response</c>, is written back as the site's response: status, headers and body.
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
        // The path below the site's root, under the address the application is asked at, and where the visitor was in
        // the forwarded headers: the mount is the site's virtual path.
        var prefix = Prefix(context.Request);
        var url = FetchProtocol.Url(_baseUri, PathBelow(context.Request.Url.AbsolutePath, prefix), context.Request.Url.Query);
        var method = context.Request.HttpMethod;
        var headers = FetchProtocol.Headers(
            Flatten(context.Request.Headers),
            context.Request.Url.Scheme,
            context.Request.Headers["Host"] ?? context.Request.Url.Authority,
            prefix);

        var body = await ReadBodyAsync(context.Request);
        var disconnected = ClientDisconnected(response);

        // Buffered, the body goes here and the head waits beside it; streamed, both are written as they come.
        var buffer = _responseBody == BodyMode.Buffered ? new MemoryStream() : null;
        FetchResponseHead? head = null;

        if (buffer is null)
            response.BufferOutput = false;

        var request = new NodeRequest(context);
        await request.RunAsync(_pool ?? AspNetNode.Pool, Module, async exports =>
        {
            var fetch = FetchProtocol.Handler(exports, Module.Name, out var app);

            var controller = FetchProtocol.AbortController();
            using var controllerReference = new JSReference(controller, isWeak: false);

            var fetchRequest = FetchProtocol.Request(url, method, headers, controller["signal"], body is null ? null : new JSTypedArray<byte>(body));

            // Called as the request, so that a fetch of the site it makes is answered in process.
            using var answer = await FetchProtocol.RespondAsync(() =>
                request.Call(fetch, app, fetchRequest, FetchProtocol.Environment(_environment), FetchProtocol.Context()));

            var answeredHead = FetchProtocol.Head(answer.GetValue());

            if (buffer is null)
                await request.InvokeAsync(() =>
                {
                    WriteHead(response, answeredHead);
                    response.Flush();
                    return true;
                });
            else
                head = answeredHead;

            // From the reference again: writing the head was an await, which ended the scope of anything made before it.
            await FetchProtocol.BodyAsync(answer.GetValue(), async bytes =>
            {
                // The visitor has gone: the render is told, and its body cancelled.
                if (disconnected.IsCancellationRequested)
                {
                    controllerReference.GetValue().CallMethod("abort", "the request was aborted");
                    return false;
                }

                if (buffer is not null)
                    buffer.Write(bytes, 0, bytes.Length);
                else
                    await request.InvokeAsync(() =>
                    {
                        response.OutputStream.Write(bytes, 0, bytes.Length);
                        response.Flush();
                        return true;
                    });

                return true;
            });

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
    /// Where the site is mounted: its virtual path, or empty at the root.
    /// </summary>
    /// <param name="request">The request.</param>
    static string Prefix(HttpRequest request)
    {
        return (request.ApplicationPath ?? "/").TrimEnd('/');
    }

    /// <summary>
    /// A path with the site's virtual path taken off it.
    /// </summary>
    /// <param name="path">The path, as the visitor asked for it.</param>
    /// <param name="prefix">The site's virtual path, or empty at the root.</param>
    static string PathBelow(string path, string prefix)
    {
        if (prefix.Length == 0 || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return path;

        if (path.Length == prefix.Length)
            return "/";

        return path[prefix.Length] == '/' ? path.Substring(prefix.Length) : path;
    }

    /// <summary>
    /// The request's headers, each value on its own.
    /// </summary>
    /// <param name="headers">The headers.</param>
    static IEnumerable<KeyValuePair<string, string?>> Flatten(NameValueCollection headers)
    {
        foreach (var name in headers.AllKeys)
            if (name is not null)
                foreach (var value in headers.GetValues(name) ?? [])
                    yield return new KeyValuePair<string, string?>(name, value);
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
    /// Writes the status and headers the application answered with.
    /// </summary>
    /// <remarks>
    /// IIS is told not to put its own error page in place of the application's: a 404 the application rendered is
    /// the page the visitor should see. The framing headers are left out.
    /// </remarks>
    /// <param name="response">The response.</param>
    /// <param name="head">What the application answered with.</param>
    static void WriteHead(HttpResponseBase response, FetchResponseHead head)
    {
        response.TrySkipIisCustomErrors = true;
        response.StatusCode = head.Status;
        if (head.StatusText.Length > 0)
            response.StatusDescription = head.StatusText;

        foreach (var header in head.Headers)
        {
            if (FetchProtocol.IsFramingHeader(header.Key))
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

}
