using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Web.Routing;

using Alethic.Node.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// A whole page from an application's <c>fetch</c> handler: the request it is asked, and its response written back as
/// the site's.
/// </summary>
[TestClass]
public class FetchRequestHandlerTests
{

    /// <summary>
    /// An application answering by path, and with an echo of the request for any other.
    /// </summary>
    static readonly NodeModuleSource Module = TestModules.FromText("app.cjs", """
        const encoder = new TextEncoder();

        module.exports = {
            default: {
                async fetch(request, env, ctx) {
                    const url = new URL(request.url);
                    switch (url.pathname) {
                        case '/fails':
                            throw new Error('render failed');
                        case '/stream':
                            return new Response(new ReadableStream({
                                async start(controller) {
                                    for (const part of ['a', 'b', 'c']) {
                                        await new Promise(resolve => setTimeout(resolve, 5));
                                        controller.enqueue(encoder.encode(part));
                                    }
                                    controller.close();
                                },
                            }), { headers: { 'content-type': 'text/plain' } });
                        case '/missing':
                            return new Response('not here', { status: 404, headers: { 'content-type': 'text/plain' } });
                        case '/empty':
                            return new Response(null, { status: 204 });
                        default:
                            return new Response(JSON.stringify({
                                url: request.url,
                                method: request.method,
                                proto: request.headers.get('x-forwarded-proto'),
                                host: request.headers.get('x-forwarded-host'),
                                prefix: request.headers.get('x-forwarded-prefix'),
                                env: env.name ?? null,
                                waitUntil: typeof ctx.waitUntil,
                            }), {
                                status: 201,
                                statusText: 'Made',
                                headers: { 'content-type': 'application/json; charset=utf-8', 'x-thing': 'yes', 'content-length': '999' },
                            });
                    }
                },
            },
        };
        """);

    static NodeEnginePool pool = null!;

    /// <summary>
    /// A pool of the tests' own, which the handlers are given.
    /// </summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        pool = new NodeEnginePool(new NodeEnginePoolOptions() { EngineCount = 1, MaxConcurrencyPerEngine = 8 }, NullLoggerFactory.Instance, new NoServices());
    }

    /// <summary>
    /// Stops the pool.
    /// </summary>
    [ClassCleanup]
    public static async Task Cleanup()
    {
        await pool.DisposeAsync();
    }

    /// <summary>
    /// A handler over a module, on the tests' pool.
    /// </summary>
    /// <param name="module">The module.</param>
    /// <param name="configure">Sets anything else.</param>
    static FetchRequestHandler Handler(NodeModuleSource module, Action<FetchRequestHandlerOptions>? configure = null)
    {
        var options = new FetchRequestHandlerOptions() { Pool = pool };
        configure?.Invoke(options);
        return new FetchRequestHandler(module, options);
    }

    /// <summary>
    /// Runs a handler for a GET of a URL of the site.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="url">The URL.</param>
    static async Task<RecordingResponse> GetAsync(FetchRequestHandler handler, string url)
    {
        var uri = new Uri(url);
        var context = new HttpContext(new HttpRequest("", url, uri.Query.TrimStart('?')), new HttpResponse(TextWriter.Null));
        var response = new RecordingResponse();
        await handler.ProcessRequestAsync(context, response);
        return response;
    }

    /// <summary>
    /// The application's status, headers and body are the response's; the framing is the server's own.
    /// </summary>
    [TestMethod]
    public async Task The_response_is_the_application_s()
    {
        var response = await GetAsync(Handler(Module, o => o.Environment["name"] = "test"), "http://site.test/echo");

        Assert.AreEqual(201, response.StatusCode);
        Assert.AreEqual("Made", response.StatusDescription);
        Assert.AreEqual("application/json", response.ContentType);
        Assert.AreEqual("utf-8", response.Charset);
        Assert.AreEqual("yes", response.Appended["x-thing"]);
        Assert.IsNull(response.Appended["content-length"]);
        Assert.IsTrue(response.TrySkipIisCustomErrors);

        var echo = JsonDocument.Parse(response.Body).RootElement;
        Assert.AreEqual("GET", echo.GetProperty("method").GetString());
        Assert.AreEqual("test", echo.GetProperty("env").GetString());
        Assert.AreEqual("function", echo.GetProperty("waitUntil").GetString());
    }

    /// <summary>
    /// The application is asked at the base address, not where the visitor was, which the forwarded headers tell it.
    /// </summary>
    [TestMethod]
    public async Task The_application_is_asked_at_the_base_address()
    {
        var response = await GetAsync(Handler(Module), "http://site.test/echo?x=1");
        var echo = JsonDocument.Parse(response.Body).RootElement;
        Assert.AreEqual("http://node.invalid/echo?x=1", echo.GetProperty("url").GetString());
        Assert.AreEqual("http", echo.GetProperty("proto").GetString());
        Assert.AreEqual("site.test", echo.GetProperty("host").GetString());
        Assert.AreEqual(JsonValueKind.Null, echo.GetProperty("prefix").ValueKind);

        var under = await GetAsync(Handler(Module, o => o.BaseUri = new Uri("http://app.test/under/")), "http://site.test/echo");
        Assert.AreEqual("http://app.test/under/echo", JsonDocument.Parse(under.Body).RootElement.GetProperty("url").GetString());
    }

    /// <summary>
    /// Streamed, the body is written unbuffered, each chunk flushed as it comes.
    /// </summary>
    [TestMethod]
    public async Task A_streamed_body_is_flushed_as_it_comes()
    {
        var response = await GetAsync(Handler(Module), "http://site.test/stream");

        Assert.AreEqual("abc", response.Body);
        Assert.IsFalse(response.BufferOutput);
        Assert.AreEqual(4, response.Flushes);
    }

    /// <summary>
    /// Buffered, the body is written whole once the render is done.
    /// </summary>
    [TestMethod]
    public async Task A_buffered_body_is_written_whole()
    {
        var response = await GetAsync(Handler(Module, o => o.ResponseBody = BodyMode.Buffered), "http://site.test/stream");

        Assert.AreEqual("abc", response.Body);
        Assert.IsTrue(response.BufferOutput);
        Assert.AreEqual(0, response.Flushes);
        Assert.AreEqual("text/plain", response.ContentType);
    }

    /// <summary>
    /// A status the application answered with an error page of its own is passed on with that page.
    /// </summary>
    [TestMethod]
    public async Task An_error_status_is_passed_on_with_its_page()
    {
        var response = await GetAsync(Handler(Module), "http://site.test/missing");

        Assert.AreEqual(404, response.StatusCode);
        Assert.AreEqual("not here", response.Body);
    }

    /// <summary>
    /// A response with no body writes none.
    /// </summary>
    [TestMethod]
    public async Task A_response_with_no_body_writes_none()
    {
        var response = await GetAsync(Handler(Module), "http://site.test/empty");

        Assert.AreEqual(204, response.StatusCode);
        Assert.AreEqual("", response.Body);
    }

    /// <summary>
    /// A handler that throws before it answers fails the request, with nothing written, for the site to answer.
    /// </summary>
    [TestMethod]
    public async Task A_failure_before_the_answer_fails_the_request()
    {
        var response = new RecordingResponse();
        var context = new HttpContext(new HttpRequest("", "http://site.test/fails", ""), new HttpResponse(TextWriter.Null));

        var failure = await Assert.ThrowsAsync<Exception>(() => Handler(Module).ProcessRequestAsync(context, response));
        StringAssert.Contains(failure.Message, "render failed");
        Assert.AreEqual(200, response.StatusCode);
        Assert.AreEqual("", response.Body);
    }

    /// <summary>
    /// A bare function as the default export is the handler itself.
    /// </summary>
    [TestMethod]
    public async Task A_bare_function_is_the_handler()
    {
        var module = TestModules.FromText("bare.cjs", "module.exports = async request => new Response('bare ' + new URL(request.url).pathname);");
        var response = await GetAsync(Handler(module), "http://site.test/here");

        Assert.AreEqual("bare /here", response.Body);
    }

    /// <summary>
    /// A module with no <c>fetch</c> is refused.
    /// </summary>
    [TestMethod]
    public async Task A_module_with_no_fetch_is_refused()
    {
        var module = TestModules.FromText("nothing.cjs", "module.exports = { nothing: 1 };");
        var failure = await Assert.ThrowsAsync<Exception>(() => GetAsync(Handler(module), "http://site.test/"));
        StringAssert.Contains(failure.Message, "no default export with a fetch function");
    }

    /// <summary>
    /// <c>MapNode</c> adds a route whose requests go to the handler.
    /// </summary>
    [TestMethod]
    public void MapNode_adds_a_route_to_the_handler()
    {
        var routes = new RouteCollection();
        var handler = Handler(Module);

        var route = routes.MapNode("parks/{parkRef}", handler);

        Assert.AreSame(route, routes.Single());
        Assert.AreEqual("parks/{parkRef}", route.Url);
        Assert.AreSame(handler, route.RouteHandler.GetHttpHandler(null!));
    }

    /// <summary>
    /// Supplies nothing.
    /// </summary>
    sealed class NoServices : IServiceProvider
    {

        /// <summary>
        /// Supplies nothing.
        /// </summary>
        /// <param name="serviceType">What is asked for.</param>
        public object? GetService(Type serviceType) => null;

    }

}
