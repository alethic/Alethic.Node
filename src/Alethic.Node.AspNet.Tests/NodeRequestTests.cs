using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Alethic.Node.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// Node work done on behalf of a request: what it hands back is done as the request, and a <c>fetch</c> of the site is
/// answered in process by the site's handlers.
/// </summary>
[TestClass]
public class NodeRequestTests
{

    /// <summary>
    /// What the tests call: a <c>fetch</c> of the site, in each of the ways a page's script might.
    /// </summary>
    static readonly NodeModuleSource Module = TestModules.FromText("requests.cjs", """
        module.exports = {
            async get(path) {
                const response = await fetch(path);
                return `${response.status}|${response.headers.get('content-type')}|${await response.text()}`;
            },
            async post(path) {
                try {
                    await fetch(path, { method: 'POST' });
                    return 'sent';
                } catch (e) {
                    return e.message;
                }
            },
            async later(path, ms) {
                await new Promise(resolve => setTimeout(resolve, ms));
                return await (await fetch(path)).text();
            },
            fail() {
                throw new Error('boom');
            },
        };
        """);

    static NodeEnginePool pool = null!;

    /// <summary>
    /// The application's pool, as a site would have it.
    /// </summary>
    static NodeEnginePool Pool => AspNetNode.Pool;

    /// <summary>
    /// Gives the application a pool through <see cref="HttpRuntime.WebObjectActivator"/>, as a site with a container
    /// would: one engine, so that requests share it, and nothing done to prepare it, which requests do themselves.
    /// </summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        pool = new NodeEnginePool(new NodeEnginePoolOptions() { EngineCount = 1, MaxConcurrencyPerEngine = 8 }, NullLoggerFactory.Instance, new NoServices());
        HttpRuntime.WebObjectActivator = new Supplies(pool);
    }

    /// <summary>
    /// Stops the pool, and takes the activator away.
    /// </summary>
    [ClassCleanup]
    public static async Task Cleanup()
    {
        HttpRuntime.WebObjectActivator = null;
        await pool.DisposeAsync();
    }

    /// <summary>
    /// The application's pool is the one its activator supplies.
    /// </summary>
    [TestMethod]
    public void The_pool_is_the_one_the_activator_supplies()
    {
        Assert.AreSame(pool, AspNetNode.Pool);
    }

    /// <summary>
    /// A request for a page of the site, made by a visitor with a cookie.
    /// </summary>
    /// <param name="cookie">The value of the visitor's <c>visitor</c> cookie.</param>
    static HttpContext Context(string cookie = "someone")
    {
        var request = new HttpRequest("", "http://site.test/page.aspx", "");
        request.Cookies.Add(new HttpCookie("visitor", cookie));
        return new HttpContext(request, new HttpResponse(TextWriter.Null)) { User = new GenericPrincipal(new GenericIdentity(cookie), []) };
    }

    /// <summary>
    /// Calls an export as the request, and awaits the string it promises.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="exports">The module's exports.</param>
    /// <param name="name">The export.</param>
    /// <param name="args">Its arguments.</param>
    static async Task<string> CallAsync(NodeRequest request, JSValue exports, string name, params JSValue[] args)
    {
        return (string)await ((JSPromise)request.Call(exports[name], exports, args)).AsTask();
    }

    /// <summary>
    /// A handler that answers with the path and query it was asked for, and who for.
    /// </summary>
    /// <param name="answer">Makes the answer from the handler's context.</param>
    sealed class Handler(Action<HttpContext> answer) : IHttpHandler
    {

        /// <summary>
        /// Whether one instance can serve several requests.
        /// </summary>
        public bool IsReusable => false;

        /// <summary>
        /// Answers.
        /// </summary>
        /// <param name="context">The request.</param>
        public void ProcessRequest(HttpContext context) => answer(context);

    }

    /// <summary>
    /// A <c>fetch</c> of a path of the site is answered by the handler the path maps to, with its status, content type
    /// and body.
    /// </summary>
    [TestMethod]
    public async Task A_fetch_of_the_site_is_answered_by_its_handler()
    {
        var request = new NodeRequest(Context())
        {
            Handlers = (context, path) => path == "thing" ? new Handler(c =>
            {
                c.Response.StatusCode = 201;
                c.Response.ContentType = "text/plain";
                c.Response.Write($"{c.Request.Path}?{c.Request.QueryString}");
            }) : null,
        };

        var answer = await request.RunAsync(Pool, Module, exports => CallAsync(request, exports, "get", "/thing?x=1"));
        Assert.AreEqual("201|text/plain|/thing?x=1", answer);
    }

    /// <summary>
    /// A <c>fetch</c> of the request's own origin, absolute, is answered in process as a relative one is.
    /// </summary>
    [TestMethod]
    public async Task A_fetch_of_the_request_origin_is_answered_in_process()
    {
        var request = new NodeRequest(Context()) { Handlers = (context, path) => new Handler(c => c.Response.Write(path)) };

        var answer = await request.RunAsync(Pool, Module, exports => CallAsync(request, exports, "get", "http://site.test/a/b"));
        Assert.AreEqual("200|text/html|a/b", answer);
    }

    /// <summary>
    /// The handler runs as the request: in a context of its own, current while it runs, with the request's user and
    /// cookies.
    /// </summary>
    [TestMethod]
    public async Task The_handler_runs_as_the_request()
    {
        var parent = Context("alice");
        var request = new NodeRequest(parent)
        {
            Handlers = (context, path) => new Handler(c =>
            {
                Assert.AreSame(c, HttpContext.Current);
                Assert.AreNotSame(parent, c);
                c.Response.Write($"{c.User.Identity.Name}|{c.Request.Cookies["visitor"]?.Value}");
            }),
        };

        var answer = await request.RunAsync(Pool, Module, exports => CallAsync(request, exports, "get", "/who"));
        Assert.AreEqual("200|text/html|alice|alice", answer);
    }

    /// <summary>
    /// A path nothing handles is a 404, and a handler that throws is the status a browser would have been given.
    /// </summary>
    [TestMethod]
    public async Task Unhandled_and_failed_requests_are_error_statuses()
    {
        var request = new NodeRequest(Context())
        {
            Handlers = (context, path) => path switch
            {
                "fails" => new Handler(c => throw new InvalidOperationException("handler failed")),
                "forbidden" => new Handler(c => throw new HttpException(403, "no")),
                _ => null,
            },
        };

        // Each called before anything is awaited: the exports are good only in the scope they were given in.
        var answers = await request.RunAsync(Pool, Module, exports => Task.WhenAll(
            CallAsync(request, exports, "get", "/missing"),
            CallAsync(request, exports, "get", "/fails"),
            CallAsync(request, exports, "get", "/forbidden")));

        CollectionAssert.AreEqual(new[] { "404|null|", "500|null|", "403|null|" }, answers);
    }

    /// <summary>
    /// Only <c>GET</c> and <c>HEAD</c> of the site can be answered in process; anything else is refused, not sent.
    /// </summary>
    [TestMethod]
    public async Task Other_methods_are_refused()
    {
        var handled = false;
        var request = new NodeRequest(Context()) { Handlers = (context, path) => new Handler(c => handled = true) };

        var answer = await request.RunAsync(Pool, Module, exports => CallAsync(request, exports, "post", "/thing"));
        StringAssert.Contains(answer, "Only GET and HEAD");
        Assert.IsFalse(handled);
    }

    /// <summary>
    /// Requests sharing an engine each <c>fetch</c> as themselves, however their work interleaves.
    /// </summary>
    [TestMethod]
    public async Task Requests_sharing_an_engine_fetch_as_themselves()
    {
        static NodeRequest Visitor(string name) => new(Context(name)) { Handlers = (context, path) => new Handler(c => c.Response.Write(c.Request.Cookies["visitor"]!.Value)) };

        var first = Visitor("first");
        var second = Visitor("second");

        // The first waits longer, so the second's fetch is made while the first's is still to come.
        var answers = await Task.WhenAll(
            first.RunAsync(Pool, Module, exports => CallAsync(first, exports, "later", "/who", 100)),
            second.RunAsync(Pool, Module, exports => CallAsync(second, exports, "later", "/who", 10)));

        CollectionAssert.AreEqual(new[] { "first", "second" }, answers);
    }

    /// <summary>
    /// What the work hands back runs on the request's thread, which <see cref="NodeRequest.Run{T}(NodeEnginePool,
    /// NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/> blocks to serve it.
    /// </summary>
    [TestMethod]
    public void Run_serves_invocations_on_the_request_thread()
    {
        var thread = Environment.CurrentManagedThreadId;
        var request = new NodeRequest(Context());

        var served = request.Run(Pool, Module, exports => request.InvokeAsync(() => Environment.CurrentManagedThreadId));
        Assert.AreEqual(thread, served);
    }

    /// <summary>
    /// A request that is not asynchronous holds its synchronization context while it waits, so nothing waited for may
    /// need it — starting an engine included.
    /// </summary>
    [TestMethod]
    public async Task Run_does_not_wait_on_the_request_context()
    {
        // A pool of its own, not yet started, so that starting an engine happens under the context.
        var pool = new NodeEnginePool(new NodeEnginePoolOptions() { ConfigureEngine = (_, lease) => NodeRequest.InstallAsync(lease) }, NullLoggerFactory.Instance, new NoServices());

        try
        {
            string? answer = null;
            Exception? failed = null;

            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new HeldContext());

                try
                {
                    var request = new NodeRequest(Context()) { Handlers = (context, path) => new Handler(c => c.Response.Write("served")) };
                    answer = request.Run(pool, Module, exports => CallAsync(request, exports, "get", "/x"));
                }
                catch (Exception e)
                {
                    failed = e;
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Run waited on the context its thread holds.");
            Assert.IsNull(failed, failed?.ToString());
            Assert.AreEqual("200|text/html|served", answer);
        }
        finally
        {
            await pool.DisposeAsync();
        }
    }

    /// <summary>
    /// <see cref="NodeRequest.Run{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}}, CancellationToken)"/>
    /// refuses an invocation that answers asynchronously, which only an asynchronous request can wait for.
    /// </summary>
    [TestMethod]
    public void Run_refuses_asynchronous_invocations()
    {
        var request = new NodeRequest(Context());

        Assert.ThrowsExactly<InvalidOperationException>(() => request.Run(Pool, Module, exports => request.InvokeAsync(async () =>
        {
            await Task.Delay(10);
            return 1;
        })));
    }

    /// <summary>
    /// <see cref="NodeRequest.RunAsync{T}(NodeEnginePool, NodeModuleSource, Func{JSValue, Task{T}},
    /// CancellationToken)"/> serves an invocation that answers asynchronously.
    /// </summary>
    [TestMethod]
    public async Task RunAsync_serves_asynchronous_invocations()
    {
        var request = new NodeRequest(Context());

        var answer = await request.RunAsync(Pool, Module, exports => request.InvokeAsync(async () =>
        {
            await Task.Delay(10);
            return 1;
        }));

        Assert.AreEqual(1, answer);
    }

    /// <summary>
    /// What the work throws is what the run throws.
    /// </summary>
    [TestMethod]
    public async Task What_the_work_throws_is_thrown()
    {
        var request = new NodeRequest(Context());

        var thrown = await Assert.ThrowsAsync<JSException>(() => request.RunAsync(Pool, Module, exports => Task.FromResult((string)request.Call(exports["fail"], exports))));
        StringAssert.Contains(thrown.Message, "boom");
    }

    /// <summary>
    /// Work handed back once the run is over fails, rather than waiting for a request that is gone.
    /// </summary>
    [TestMethod]
    public async Task Invocations_after_the_run_fail()
    {
        var request = new NodeRequest(Context());
        await request.RunAsync(Pool, Module, exports => Task.FromResult(true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => request.InvokeAsync(() => 1));
    }

    /// <summary>
    /// A synchronization context whose thread is always busy: what is posted to it never runs.
    /// </summary>
    sealed class HeldContext : SynchronizationContext
    {

        /// <summary>
        /// Drops the callback, as a context held by a blocked thread would never get to it.
        /// </summary>
        /// <param name="d">The callback.</param>
        /// <param name="state">Its state.</param>
        public override void Post(SendOrPostCallback d, object? state)
        {

        }

        /// <summary>
        /// Refuses, as a context held by a blocked thread would deadlock.
        /// </summary>
        /// <param name="d">The callback.</param>
        /// <param name="state">Its state.</param>
        public override void Send(SendOrPostCallback d, object? state)
        {
            throw new InvalidOperationException("The context's thread is held.");
        }

    }

    /// <summary>
    /// An activator that supplies one pool, and nothing else.
    /// </summary>
    /// <param name="supplied">The pool.</param>
    sealed class Supplies(NodeEnginePool supplied) : IServiceProvider
    {

        /// <summary>
        /// The pool, where it is asked for.
        /// </summary>
        /// <param name="serviceType">The service asked for.</param>
        public object? GetService(Type serviceType) => serviceType == typeof(NodeEnginePool) ? supplied : null;

    }

    /// <summary>
    /// A provider of no services.
    /// </summary>
    sealed class NoServices : IServiceProvider
    {

        /// <summary>
        /// Provides nothing.
        /// </summary>
        /// <param name="serviceType">The service asked for.</param>
        public object? GetService(Type serviceType) => null;

    }

}
