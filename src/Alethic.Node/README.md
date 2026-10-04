# Alethic.Node

Node embedded in a .NET process, through [node-api-dotnet](https://github.com/microsoft/node-api-dotnet). No
sidecar process, no IPC. It is libnode, and the package says so: there is no runtime abstraction here.

For any host: .NET 8 and 9, and .NET Framework 4.7.2 and later, so an ASP.NET Web Forms application can use it as
well as a console or a service. [Alethic.Node.AspNetCore](https://www.nuget.org/packages/Alethic.Node.AspNetCore)
builds server-side rendering for ASP.NET Core on it.

## Two things

**Engines** run JavaScript. A `NodeEnginePool` holds several, each a libnode runtime on its own thread. It owns the
threads, and the whole application shares one.

**Modules** are Node's. A module is loaded with `require` and cached in `require.cache` by resolved filename, so it
evaluates once per engine and keeps its module scope — the identity a module has in any other Node program. This
library caches nothing of its own.

```csharp
services.AddNodeEnginePool(o =>
{
    o.EngineCount = 4;                 // must track the CPU limit; see remarks on the option
    o.MaxConcurrencyPerEngine = 4;     // backpressure, not mutual exclusion
});

var pool = provider.GetRequiredService<NodeEnginePool>();

var result = await pool.RunAsync(NodeModuleSource.FromFile("tool.cjs"), async exports =>
    (int)await ((JSPromise)exports.CallMethod("transform", input)).AsTask());
```

A one-shot puts you on an engine's thread writing ordinary node-api-dotnet. For several steps that must share one
engine — or a claim that outlives a single call — take a lease instead: `await using var lease = await
pool.AcquireAsync();` and run against it. A lease is a capacity claim and an affinity pin, not exclusivity; engines
overlap many concurrent calls anyway.

Without a container, construct the pool from its options, a logger factory and a service provider —
`NullLoggerFactory.Instance`, and whatever `IServiceProvider` the host has, which `ConfigureEngine` is handed — and
dispose of it when the host stops.

## Learning the limits

A pool is `Fixed` by default: `EngineCount` engines, each holding up to `MaxConcurrencyPerEngine` leases. An
`Adaptive` pool learns both within bounds you set, reading itself every `AdaptInterval` (a second):

```csharp
services.AddNodeEnginePool(o =>
{
    o.Mode = NodeEnginePoolMode.Adaptive;
    o.MinEngineCount = 1;              // and EngineCount, the most: still the CPU limit
    o.EngineCount = 4;
    o.MinConcurrencyPerEngine = 1;     // and MaxConcurrencyPerEngine, the most, where each engine starts
    o.MaxConcurrencyPerEngine = 32;
    o.TargetEventLoopDelay = TimeSpan.FromMilliseconds(40);
});
```

- **Each engine's limit follows its event-loop delay:** how long work posted to the engine waits before its thread
  runs it, measured from the leases themselves, each stamped when it is posted and when the thread starts it, so an
  idle engine measures nothing and a busy one is measured by all it does. A lease's latency would say little, since
  much of a lease is spent waiting on things that leave the thread free. Over `TargetEventLoopDelay`, the limit falls by the target over
  the delay, by no more than half; under it, where the limit was reached, it rises by its square root. Work that only
  waits climbs to many leases an engine; work that keeps the thread busy settles near one.
- **The number of engines is found by hill climbing:** where acquisitions waited for capacity, the pool tries one more
  engine, keeps it if the leases it completes a second rose by a tenth, and retires it otherwise. An engine that adds
  throughput has a core and work of its own; one that does not is only another thread contending. Engines idle for
  `EngineIdleTimeout` (thirty seconds) retire down to `MinEngineCount`.

`GetStatistics()` reports what it has learned: each engine's load, limit and delay, and the acquisitions waiting.

## Overload

Acquisitions with no capacity wait in line, first come first served, up to `AcquireTimeout`. Set `OverloadInterval`,
in either mode, to fail fast under a standing queue instead: once the line has gone that long without emptying, the
pool counts itself overloaded, waits only `OverloadAcquireTimeout` (a hundred milliseconds), and refuses acquisitions
that have waited longer than that rather than serving them late. A burst that clears within the interval is waited out
as before.

## Constraints worth knowing

- **Reference the RID-specific `Microsoft.JavaScript.LibNode.<rid>` package** for each runtime you deploy to. The
  umbrella package depends on every platform at once and lands ~640 MB of native libraries in the output. Where the
  library is neither beside the application nor under `runtimes/<rid>/native` — a web application on .NET Framework,
  whose base directory is the site and not `bin` — set `LibNodePath`.
- **Engine count must be configured, never derived.** Inside a container the processor count reports the host's
  cores, not the quota. One engine already overlaps many concurrent calls, because everything a module awaits yields
  to its event loop; engines exist for CPU parallelism.
- **One Node per process.** Node starts once per process and cannot start again, so every pool in a process shares
  one platform. Where a process holds more than one copy of this assembly — ASP.NET on .NET Framework restarting an
  application in a new AppDomain of the same process — a copy that finds the library already loaded by another refuses
  to start, on Windows, rather than fail natively. Keep such hosts from restarting applications in place.
- **CommonJS only.** The embedded runtime registers no dynamic-import callback, so ES modules and `import()` do not
  resolve. Bundle fully static — for esbuild, `--format=cjs` with code splitting off.
- **Module scope is shared across concurrent calls on an engine**, since the module is loaded once and reused.
  Per-call state belongs in the call — `AsyncLocalStorage`, say — not at module scope.
- **A rebuilt module is not picked up.** `require.cache` holds a module for the runtime's life, so rebuilding while the
  host runs changes nothing until restart.
