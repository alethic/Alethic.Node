using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// The modules an engine loads, on the Node the engines embed: CommonJS, and ES modules, through <c>require</c> where
/// their graph is synchronous and through <c>import()</c> where it awaits at its top level; and <c>import()</c> inside a
/// module, as a bundle split into chunks does.
/// </summary>
[TestClass]
public class NodeModuleFormatTests
{

    /// <summary>
    /// Counts in module scope, as an ES module.
    /// </summary>
    static readonly NodeModuleSource EsCounter = TestModules.FromText("counter.mjs", """
        let count = 0;
        export function next() { return ++count; }
        export default 'counter';
        """, ".mjs");

    /// <summary>
    /// Counts in module scope, as an ES module that awaits a timer at its top level before it is ready.
    /// </summary>
    static readonly NodeModuleSource EsAwaitingCounter = TestModules.FromText("awaiting-counter.mjs", """
        let count = 0;
        await new Promise(resolve => setTimeout(resolve, 10));
        export function next() { return ++count; }
        """, ".mjs");

    /// <summary>
    /// A chunk, as a bundler splits one off: an ES module.
    /// </summary>
    static readonly NodeModuleSource Chunk = TestModules.FromText("chunk.mjs", """
        export const answer = 42;
        """, ".mjs");

    /// <summary>
    /// A CommonJS module that loads a chunk with <c>import()</c> when called.
    /// </summary>
    static readonly NodeModuleSource Splitting = TestModules.FromText("splitting.cjs", """
        const { pathToFileURL } = require('url');
        module.exports.answer = async path => (await import(pathToFileURL(path).href)).answer;
        """);

    /// <summary>
    /// Builds a provider with a one-engine pool registered.
    /// </summary>
    static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddNodeEnginePool(o => o.EngineCount = 1);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// An ES module loads as a CommonJS one does: its namespace is the exports, and its scope is kept across calls.
    /// </summary>
    [TestMethod]
    public async Task An_ES_module_without_top_level_await_loads_through_require()
    {
        await using var services = BuildServices();
        var pool = services.GetRequiredService<NodeEnginePool>();

        Assert.AreEqual(1, await pool.RunAsync(EsCounter, exports => Task.FromResult((int)exports.CallMethod("next"))));
        Assert.AreEqual(2, pool.Run(EsCounter, exports => (int)exports.CallMethod("next")));
        Assert.AreEqual("counter", await pool.RunAsync(EsCounter, exports => Task.FromResult((string)exports["default"])));
    }

    /// <summary>
    /// An ES module that awaits at its top level is imported, once it is ready, and keeps its scope across calls as any
    /// other module does: one instance per engine.
    /// </summary>
    [TestMethod]
    public async Task An_ES_module_with_top_level_await_is_imported()
    {
        await using var services = BuildServices();
        var pool = services.GetRequiredService<NodeEnginePool>();

        Assert.AreEqual(1, await pool.RunAsync(EsAwaitingCounter, exports => Task.FromResult((int)exports.CallMethod("next"))));
        Assert.AreEqual(2, await pool.RunAsync(EsAwaitingCounter, exports => Task.FromResult((int)exports.CallMethod("next"))));

        // Loading it ahead of use, too.
        await using var lease = await pool.AcquireAsync();
        await lease.ImportAsync(EsAwaitingCounter);
        Assert.AreEqual(3, await lease.RunAsync(EsAwaitingCounter, exports => Task.FromResult((int)exports.CallMethod("next"))));
    }

    /// <summary>
    /// Synchronously, an ES module that awaits at its top level cannot be had, and Node says so, rather than handing
    /// over a module half evaluated.
    /// </summary>
    [TestMethod]
    public async Task An_ES_module_with_top_level_await_is_refused_synchronously()
    {
        var module = TestModules.FromText("awaits.mjs", """
            await Promise.resolve();
            export const ready = true;
            """, ".mjs");

        await using var services = BuildServices();
        var pool = services.GetRequiredService<NodeEnginePool>();

        var thrown = Assert.ThrowsExactly<JSException>(() => pool.Run(module, exports => (bool)exports["ready"]));
        StringAssert.Contains(thrown.Message, "top-level await");
    }

    /// <summary>
    /// A module can <c>import()</c> another from disk: a bundle need not be one file.
    /// </summary>
    [TestMethod]
    public async Task A_module_can_import_another()
    {
        await using var services = BuildServices();
        var pool = services.GetRequiredService<NodeEnginePool>();

        var chunk = await Chunk.ResolveAsync(default);
        Assert.AreEqual(42, await pool.RunAsync(Splitting, async exports => (int)await ((JSPromise)exports.CallMethod("answer", chunk)).AsTask()));
    }

}
