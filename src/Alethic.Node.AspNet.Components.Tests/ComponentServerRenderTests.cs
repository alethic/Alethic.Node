using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Alethic.Node.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Components.Tests;

/// <summary>
/// The server render against a server module whose <c>render</c> knows no framework: components found by name, props
/// marshalled, commands raised on the request, a page's state shared, failures carried back, and the site fetched in
/// process.
/// </summary>
[TestClass]
public class ComponentServerRenderTests
{

    /// <summary>
    /// A server module whose components are functions answering their HTML, and whose <c>render</c> calls them.
    /// </summary>
    static readonly NodeModuleSource Bundle = TestModules.FromText("bundle.cjs", """
        const Echo = props => JSON.stringify(props, (k, v) => typeof v === 'function' ? `fn:${v.name}` : v);

        async function Command(props) {
            return JSON.stringify(await props.onGo(1, 'two')) ?? 'undefined';
        }

        async function Fetch() {
            return await (await fetch('/data?x=1')).text();
        }

        // Calls its callback and walks away, as a component may.
        function Ignores(props) {
            props.onGo().then(() => undefined);
            return 'ignored';
        }

        // Counts the components of the page it rendered in.
        function Counts(props, page) {
            page.count = (page.count ?? 0) + 1;
            return String(page.count);
        }

        const NotHtml = () => 42;

        module.exports = {
            Echo,
            Command,
            Fetch,
            Ignores,
            Counts,
            NotHtml,
            Nested: { Deeper: { Echo } },
            async render(component, props, page) {
                return await component(props, page);
            },
        };
        """);

    static NodeEnginePool pool = null!;

    /// <summary>
    /// Starts a pool prepared for requests.
    /// </summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        pool = new NodeEnginePool(new NodeEnginePoolOptions() { ConfigureEngine = (_, lease) => NodeRequest.InstallAsync(lease) }, NullLoggerFactory.Instance, new NoServices());
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
    /// A request for a page.
    /// </summary>
    static NodeRequest Request()
    {
        return new NodeRequest(new HttpContext(new HttpRequest("", "http://site.test/page.aspx", ""), new HttpResponse(TextWriter.Null)));
    }

    /// <summary>
    /// An outlet whose commands are answered by <paramref name="raise"/>.
    /// </summary>
    static ComponentOutlet Outlet(string id, string component, ComponentObject props, Func<string, IReadOnlyList<JsonElement>, Task<string?>>? raise = null)
    {
        return new ComponentOutlet(id, component, props, raise ?? ((name, args) => Task.FromResult<string?>(null)));
    }

    /// <summary>
    /// Props reach the bundle as JavaScript values: scalars, objects, arrays, and each command a function named for it.
    /// </summary>
    [TestMethod]
    public async Task Props_reach_the_bundle_as_JavaScript()
    {
        var props = new ComponentObject
        {
            { "title", "<b>" },
            { "count", 1.5 },
            { "none", null },
            { "nested", new ComponentObject { { "list", new ComponentArray { true, new ComponentCommand("Pick") } } } },
            { "onGo", new ComponentCommand("Go") },
        };

        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "Echo", props)], TimeSpan.FromSeconds(30));
        Assert.AreEqual("""{"title":"<b>","count":1.5,"none":null,"nested":{"list":[true,"fn:Pick"]},"onGo":"fn:Go"}""", rendered["a"].Html);
    }

    /// <summary>
    /// A name is a dotted path through the module's exports, and the component found is what <c>render</c> is given.
    /// </summary>
    [TestMethod]
    public async Task A_name_is_a_path_through_the_exports()
    {
        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "Nested.Deeper.Echo", new ComponentObject { { "x", 1 } })], TimeSpan.FromSeconds(30));
        Assert.AreEqual("""{"x":1}""", rendered["a"].Html);
    }

    /// <summary>
    /// A name the module has nothing at fails that component, naming it, and leaves the others to render.
    /// </summary>
    [TestMethod]
    public async Task A_name_the_module_lacks_fails_its_component()
    {
        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "Nested.Missing", []), Outlet("b", "Echo", [])], TimeSpan.FromSeconds(30));

        Assert.IsNull(rendered["a"].Html);
        StringAssert.Contains(rendered["a"].Error!.Message, "Nested.Missing");
        Assert.AreEqual("{}", rendered["b"].Html);
    }

    /// <summary>
    /// A command is raised on the request's thread with what the component called it with, and its result is what the
    /// component's promise resolves to.
    /// </summary>
    [TestMethod]
    public async Task A_command_is_raised_on_the_request()
    {
        var thread = 0;
        string? raised = null;
        var outlet = Outlet("a", "Command", new ComponentObject { { "onGo", new ComponentCommand("Go") } }, (name, args) =>
        {
            thread = Environment.CurrentManagedThreadId;
            raised = $"{name}({string.Join(",", args.Select(i => i.GetRawText()))})";
            return Task.FromResult<string?>("""{"ok":true}""");
        });

        var rendered = ComponentServerRender.Render(Request(), pool, Bundle, [outlet], TimeSpan.FromSeconds(30));

        Assert.AreEqual(Environment.CurrentManagedThreadId, thread);
        Assert.AreEqual("""Go(1,"two")""", raised);
        Assert.AreEqual("""{"ok":true}""", rendered["a"].Html);
    }

    /// <summary>
    /// A command answered asynchronously is awaited on an asynchronous render.
    /// </summary>
    [TestMethod]
    public async Task An_asynchronous_command_is_awaited()
    {
        var outlet = Outlet("a", "Command", new ComponentObject { { "onGo", new ComponentCommand("Go") } }, async (name, args) =>
        {
            await Task.Delay(10);
            return "42";
        });

        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [outlet], TimeSpan.FromSeconds(30));
        Assert.AreEqual("42", rendered["a"].Html);
    }

    /// <summary>
    /// A command whose handler threw rejects with an error that leads back to the exception, which the component's error
    /// carries.
    /// </summary>
    [TestMethod]
    public async Task What_a_command_throws_is_carried_back()
    {
        var thrown = new InvalidOperationException("handler failed");
        var outlet = Outlet("a", "Command", new ComponentObject { { "onGo", new ComponentCommand("Go") } }, (name, args) => throw thrown);

        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [outlet], TimeSpan.FromSeconds(30));

        Assert.IsNull(rendered["a"].Html);
        Assert.AreEqual("handler failed", rendered["a"].Error!.Message);
        Assert.AreSame(thrown, rendered["a"].Error!.Exception);
    }

    /// <summary>
    /// A component's fetch of the site is answered in process, as the request.
    /// </summary>
    [TestMethod]
    public async Task A_fetch_of_the_site_is_answered_in_process()
    {
        var request = Request();
        request.Handlers = (context, path) => new Handler(c => c.Response.Write($"{path}?{c.Request.QueryString}"));

        var rendered = await ComponentServerRender.RenderAsync(request, pool, Bundle, [Outlet("a", "Fetch", [])], TimeSpan.FromSeconds(30));
        Assert.AreEqual("data?x=1", rendered["a"].Html);
    }

    /// <summary>
    /// A command's rejection the component leaves unhandled fails the component, after its answer comes, however late,
    /// carrying the handler's exception.
    /// </summary>
    [TestMethod]
    public async Task A_rejection_left_unhandled_fails_the_component()
    {
        var thrown = new InvalidOperationException("handler failed");
        var outlet = Outlet("a", "Ignores", new ComponentObject { { "onGo", new ComponentCommand("Go") } }, async (name, args) =>
        {
            await Task.Delay(50);
            throw thrown;
        });

        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [outlet, Outlet("b", "Echo", [])], TimeSpan.FromSeconds(30));

        Assert.IsNull(rendered["a"].Html);
        Assert.AreSame(thrown, rendered["a"].Error!.Exception);
        Assert.AreEqual("{}", rendered["b"].Html);
    }

    /// <summary>
    /// The components of one page share one page object; another page has its own.
    /// </summary>
    [TestMethod]
    public async Task A_page_shares_one_page_object()
    {
        var first = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "Counts", []), Outlet("b", "Counts", [])], TimeSpan.FromSeconds(30));
        var second = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "Counts", [])], TimeSpan.FromSeconds(30));

        Assert.AreEqual("1", first["a"].Html);
        Assert.AreEqual("2", first["b"].Html);
        Assert.AreEqual("1", second["a"].Html);
    }

    /// <summary>
    /// A <c>render</c> that resolves to something other than HTML fails the component.
    /// </summary>
    [TestMethod]
    public async Task A_render_that_is_not_HTML_fails_the_component()
    {
        var rendered = await ComponentServerRender.RenderAsync(Request(), pool, Bundle, [Outlet("a", "NotHtml", [])], TimeSpan.FromSeconds(30));
        StringAssert.Contains(rendered["a"].Error!.Message, "not a string of HTML");
    }

    /// <summary>
    /// A module without <c>render</c> fails the render.
    /// </summary>
    [TestMethod]
    public async Task A_module_without_render_fails_the_render()
    {
        var bundle = TestModules.FromText("empty.cjs", "module.exports = {};");

        // Thrown on the engine's thread, which node-api-dotnet hands back wrapped.
        var thrown = await Assert.ThrowsAsync<Exception>(() => ComponentServerRender.RenderAsync(Request(), pool, bundle, [Outlet("a", "Echo", [])], TimeSpan.FromSeconds(30)));
        StringAssert.Contains(thrown.Message, "exports no render function");
    }

    /// <summary>
    /// A render that takes too long fails with a timeout.
    /// </summary>
    [TestMethod]
    public async Task A_slow_render_times_out()
    {
        var outlet = Outlet("a", "Command", new ComponentObject { { "onGo", new ComponentCommand("Go") } }, async (name, args) =>
        {
            await Task.Delay(Timeout.Infinite);
            return null;
        });

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => ComponentServerRender.RenderAsync(Request(), pool, Bundle, [outlet], TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>
    /// A handler that answers with what its context makes.
    /// </summary>
    /// <param name="answer">Answers.</param>
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
