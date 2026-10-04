using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// Engines on their own, apart from a pool: each its own runtime on its own thread, in one process with the others.
/// </summary>
[TestClass]
public class NodeEngineTests
{

    /// <summary>
    /// Counts in module scope, at once or after a delay.
    /// </summary>
    static readonly NodeModuleSource Counter = TestModules.FromText("counter.cjs", """
        let count = 0;
        module.exports.next = () => ++count;
        module.exports.later = ms => new Promise(resolve => setTimeout(() => resolve(++count), ms));
        """);

    /// <summary>
    /// Starts an engine on the process's one Node platform, as the pool does.
    /// </summary>
    static NodeEngine Start()
    {
        var platform = NodeRuntimeHost.GetOrCreate(LibNodeLocator.Locate(null));
        return new NodeEngine(platform, AppContext.BaseDirectory, NullLogger.Instance);
    }

    /// <summary>
    /// Takes the counter's next value on an engine.
    /// </summary>
    /// <param name="engine">The engine.</param>
    static Task<int> NextAsync(NodeEngine engine)
    {
        return engine.RunAsync(Counter, exports => Task.FromResult((int)exports.CallMethod("next")), default);
    }

    /// <summary>
    /// One engine stops while another is in the middle of work: the other finishes it, and keeps its module scope; and
    /// a new engine starts beside it, in the same process, with module scope of its own. What a pool that retires idle
    /// engines, and starts them again under load, depends on.
    /// </summary>
    [TestMethod]
    public async Task An_engine_stops_while_another_works_and_a_new_one_starts_beside_it()
    {
        var stopped = Start();
        var working = Start();

        try
        {
            Assert.AreEqual(1, await NextAsync(stopped));
            Assert.AreEqual(1, await NextAsync(working));

            // Under way on one engine across the other's stopping: a timer, so it is still pending when the stop happens.
            var pending = working.RunAsync(Counter, async exports => (int)await ((JSPromise)exports.CallMethod("later", 300)).AsTask(), default);

            await stopped.DisposeAsync();

            Assert.AreEqual(2, await pending);
            Assert.AreEqual(3, await NextAsync(working));

            // A new engine, after one has stopped: its own runtime, so the module evaluates afresh.
            await using var started = Start();
            Assert.AreEqual(1, await NextAsync(started));
            Assert.AreEqual(4, await NextAsync(working));
        }
        finally
        {
            await stopped.DisposeAsync();
            await working.DisposeAsync();
        }
    }

}
