using Alethic.Node;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sample.Load;

// An adaptive pool under a workload that changes shape, watched as it learns: a line a second of what it has decided,
// the pool's own log of why, and at the end of each phase what it should have done, checked. Exits 1 where it did not.

var engineCap = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

var services = new ServiceCollection();
services.AddLogging(logging => logging
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    })
    // The pool's own log says why it decided what it did; the engines' says what each call required, which is too much.
    .AddFilter("Alethic.Node.NodeEnginePool", LogLevel.Debug)
    .SetMinimumLevel(LogLevel.Warning));

services.AddNodeEnginePool(o =>
{
    o.Mode = NodeEnginePoolMode.Adaptive;
    o.MinEngineCount = 1;
    o.EngineCount = engineCap;
    o.MinConcurrencyPerEngine = 1;
    o.MaxConcurrencyPerEngine = 32;
    o.TargetEventLoopDelay = TimeSpan.FromMilliseconds(40);
    o.EngineIdleTimeout = TimeSpan.FromSeconds(10);
    o.AcquireTimeout = TimeSpan.FromSeconds(10);
    o.OverloadInterval = TimeSpan.FromSeconds(2);
    o.OverloadAcquireTimeout = TimeSpan.FromMilliseconds(100);
});

await using var provider = services.BuildServiceProvider();

// Listening before the first engine starts, so the counters have it.
using var counters = new Counters();

var pool = provider.GetRequiredService<NodeEnginePool>();
var module = NodeModuleSource.FromFile(Path.Combine(AppContext.BaseDirectory, "work.cjs"));
await pool.PrepareAsync(lease => lease.ImportAsync(module));

var workload = new Workload(pool, module);

Console.WriteLine($"{Environment.ProcessorCount} processors; up to {engineCap} engines, 1 to 32 leases each, a 40 ms target.");
Console.WriteLine();

// The phases, each a shape of work and what the pool should make of it.
var waiting = new[] { new Call("fetch", 30) };
var computing = new[] { new Call("compute", 20) };
var mixed = new[] { new Call("fetch", 30), new Call("compute", 10), new Call("fetch", 30), new Call("compute", 10) };
var allocating = new[] { new Call("allocate", 64) };
var releasing = new[] { new Call("release", 0) };

var phases = new Phase[]
{
    new("idle", TimeSpan.FromSeconds(3), 0, [], o =>
    [
        ("one engine, nothing waiting", o.Final.Engines.Count == 1 && o.Final.Queued == 0),
        ("the loop within the target, with only the warm-up to wait for", o.MaxLoopDelay <= 40),
    ]),

    new("waiting", TimeSpan.FromSeconds(10), 48, waiting, o =>
    [
        ("the loop kept up", o.MaxLoopDelay <= 80),
        ("the limit stayed high", o.Final.Engines.All(e => e.Limit >= 16)),
        ("work got done", o.Completed > 1000),
        ("nothing refused or failed", o.Refused == 0 && o.Failed == 0),
    ]),

    new("computing", TimeSpan.FromSeconds(12), 32, computing, o =>
    [
        ("the loop fell behind, and the limit fell with it", o.MaxLoopDelay > 40 && o.Final.Engines.All(e => e.Limit <= 8)),
        ("more engines were tried and kept", engineCap == 1 || o.Final.Engines.Count >= 2),
        ("nothing failed", o.Failed == 0),
    ]),

    new("mixed", TimeSpan.FromSeconds(10), 32, mixed, o =>
    [
        ("work got done", o.Completed > 500),
        ("nothing failed", o.Failed == 0),
    ]),

    new("overload", TimeSpan.FromSeconds(8), 128, new[] { new Call("compute", 50) }, o =>
    [
        ("the pool saw it was overloaded", o.OverloadedSeen),
        ("callers were refused fast rather than served late", o.Refused > 0),
        ("work still got done", o.Completed > 0),
        ("nothing failed", o.Failed == 0),
    ]),

    new("allocating", TimeSpan.FromSeconds(4), engineCap * 2, allocating, o =>
    [
        ("an engine's heap grew by what it kept", o.MaxHeapUsed >= 48L * 1024 * 1024),
    ]),

    new("releasing", TimeSpan.FromSeconds(2), engineCap * 2, releasing, o =>
    [
        ("nothing failed", o.Failed == 0),
    ]),

    new("quiet", TimeSpan.FromSeconds(16), 0, [], o =>
    [
        ("idle engines retired", o.Final.Engines.Count == 1),
        ("nothing waiting", o.Final.Queued == 0),
    ]),
};

var failures = 0;
var clock = System.Diagnostics.Stopwatch.StartNew();

foreach (var phase in phases)
{
    Console.WriteLine($"== {phase.Name}: {phase.Callers} callers for {phase.Duration.TotalSeconds:0} s");

    var observation = new Observation();
    var completedBefore = workload.Completed;
    var refusedBefore = workload.Refused;
    var failedBefore = workload.Failed;

    using var stop = new CancellationTokenSource();
    var running = phase.Callers > 0 ? workload.RunAsync(phase.Callers, phase.Calls, stop.Token) : Task.CompletedTask;

    var phaseStarted = clock.Elapsed;
    var lastCompleted = workload.Completed;

    while (clock.Elapsed - phaseStarted < phase.Duration)
    {
        await Task.Delay(1000);

        var statistics = pool.GetStatistics();
        observation.Observe(statistics);

        var completedNow = workload.Completed;
        Console.WriteLine(Line(clock.Elapsed, statistics, completedNow - lastCompleted, workload.Refused - refusedBefore));
        lastCompleted = completedNow;
    }

    stop.Cancel();
    await running;

    observation.Observe(pool.GetStatistics());
    observation.Completed = workload.Completed - completedBefore;
    observation.Refused = workload.Refused - refusedBefore;
    observation.Failed = workload.Failed - failedBefore;

    foreach (var (what, ok) in phase.Expect(observation))
    {
        Console.WriteLine($"   {(ok ? "ok  " : "FAIL")} {what}");
        if (ok == false)
            failures++;
    }

    Console.WriteLine();
}

Console.WriteLine("== counters");
foreach (var (name, total) in counters.Totals)
    Console.WriteLine($"   {name,-60} {total}");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "The pool did what it should." : $"{failures} expectation(s) not met.");
return failures == 0 ? 0 : 1;

// One line of the pool as it stands.
static string Line(TimeSpan at, NodeEnginePoolStatistics statistics, long completedThisSecond, long refusedSoFar)
{
    var engines = string.Join("  ", statistics.Engines.Select(e =>
        $"#{e.Id} {e.InFlight}/{e.Limit} {e.LoopDelay,4:0}ms {e.HeapUsed / 1048576.0,4:0}MB"));

    return $"{at.TotalSeconds,5:0}s  {completedThisSecond,5}/s  queued {statistics.Queued,3}{(statistics.Overloaded ? " OVERLOADED" : "")}  refused {refusedSoFar,4}  memory {statistics.MemoryLoad,4:0.00}  {engines}";
}
