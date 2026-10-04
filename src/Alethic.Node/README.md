# Alethic.Node

Node.js embedded in a .NET process: a pool of real Node runtimes (libnode, through
[node-api-dotnet](https://github.com/microsoft/node-api-dotnet)), each on its own thread, running JavaScript modules
inside your application. No child process, no socket.

For any host: .NET 8 and 9, and .NET Framework 4.7.2 and later. The ASP.NET Core and ASP.NET packages build on it;
use this one directly in a console, a service, a job, or anywhere else you have JavaScript to run.

## Install

```shell
dotnet add package Alethic.Node
dotnet add package Microsoft.JavaScript.LibNode.win-x64    # and/or linux-x64, linux-arm64, osx-x64, osx-arm64
```

The `LibNode` package carries the native runtime for one platform and puts it under `runtimes/<rid>/native`, where the
pool finds it. Reference the platforms you deploy to; the umbrella package carries all of them, some 640 MB.

## Use

Register the pool, and run work against a module:

```csharp
services.AddNodeEnginePool(o =>
{
    o.EngineCount = 2;                 // engines are CPU parallelism: match the cores the process may use
    o.MaxConcurrencyPerEngine = 4;     // leases an engine holds at once: backpressure, not exclusion
});

var pool = provider.GetRequiredService<NodeEnginePool>();
var module = NodeModuleSource.FromFile("tools.cjs");

var slug = await pool.RunAsync(module, exports =>
    Task.FromResult((string)exports.CallMethod("slugify", "Enchanted Rock")));

var digest = await pool.RunAsync(module, async exports =>
    (string)await ((JSPromise)exports.CallMethod("digest", "enchanted rock")).AsTask());
```

Inside the callback you are on the engine's thread, writing node-api-dotnet against the module's exports: `JSValue`,
`JSPromise`, `JSReference`. Two rules from that library matter most. A `JSValue` is valid only in the scope that
produced it, and an `await` ends the scope, so anything held across one goes through a `JSReference`; and only plain
.NET data leaves the callback.

Modules are Node's own: loaded with `require`, cached per engine by filename, evaluated once, keeping their module
scope for the engine's life. The library caches nothing of its own.

**Leases.** A one-shot `RunAsync` acquires and releases capacity around one call. Where several calls must share one
engine, or the claim must outlive a call, take a lease:

```csharp
await using var lease = await pool.AcquireAsync();
var first = await lease.RunAsync(module, exports => ...);
var second = await lease.RunAsync(module, exports => ...);   // the same engine, so the same module state
```

A lease claims a share of one engine's capacity, not the engine: many leases run on an engine at once, because
everything a module awaits yields to its event loop. `Run` and `Acquire` are the synchronous forms, for work whose value
is produced synchronously on the engine's thread.

**Warm-up.** `PrepareAsync` starts the pool's engines and runs something on each, so the first request does not pay for
it:

```csharp
await pool.PrepareAsync(lease => lease.ImportAsync(module));
```

**Without a container.** Construct the pool from its options, a logger factory and a service provider
(`NullLoggerFactory.Instance` and whatever `IServiceProvider` the host has), and dispose of it when the host stops.

## Configuring

| Option | Default | |
| --- | --- | --- |
| `EngineCount` | 1 | Engines to run; adapting, the most. Match the cores the process may actually use: inside a container the processor count reports the host's. |
| `MaxConcurrencyPerEngine` | 4 | Leases an engine holds at once; adapting, the most, and where each engine's limit starts. |
| `AcquireTimeout` | 10 s | How long an acquisition waits for capacity before it fails. |
| `ConfigureEngine` | | Runs once on each engine as it starts, before anything else: a global installed, a module warmed. |
| `LibNodePath` | | Where the native runtime is, where it is not under `runtimes/<rid>/native` or beside the application. |
| `BaseDirectory` | the application's | Root for Node's package resolution, for a module that reaches a `node_modules`. |

### Adaptive mode

By default the limits are fixed at what the options say. Set `Mode` to `Adaptive` and the pool learns them, within
bounds you set, reading itself every `AdaptInterval` (a second):

```csharp
services.AddNodeEnginePool(o =>
{
    o.Mode = NodeEnginePoolMode.Adaptive;
    o.MinEngineCount = 1;
    o.EngineCount = 4;
    o.MinConcurrencyPerEngine = 1;
    o.MaxConcurrencyPerEngine = 32;
});
```

- **Each engine's limit follows its event-loop delay:** how long work posted to the engine waits before its thread
  runs it, measured from the leases themselves. Over `TargetEventLoopDelay` (40 ms) the limit falls, by no more than
  half a window; under it, where the limit was what held the work back, it rises. Work that waits on I/O settles at
  many leases an engine; work that computes settles near one.
- **The number of engines is found by trial:** where acquisitions waited for capacity, the pool starts one more
  engine, keeps it if throughput rose by a tenth, and retires it otherwise. Engines idle for `EngineIdleTimeout`
  (30 s) retire down to `MinEngineCount`. A new engine starts at the limit the others have learned.
- **Scarce memory stops the growth:** above `MemoryLoadLimit` (0.9 of the memory the process may use, by everything on
  the machine or in the container) no engine is started. Every engine is a heap the .NET garbage collector does not
  see.

### Overload

Acquisitions with no capacity wait in line, first come first served, up to `AcquireTimeout`. Set `OverloadInterval`
to fail fast under a standing queue instead: once the line has gone that long without emptying, the pool counts
itself overloaded, waits only `OverloadAcquireTimeout` (100 ms), and refuses acquisitions that have waited longer than
that rather than serving them late. A burst that clears within the interval is waited out. Works in either mode.

## Watching it

`GetStatistics()` answers at once, without touching any engine's thread: for the pool, the acquisitions waiting,
whether it is overloaded and the memory load; for each engine, by a stable id, its leases, limit, event-loop delay,
and heap in use, committed and limit, plus the memory its objects hold outside the heap. The heap figures are V8's,
read on the engine's thread as its work starts, and are the only account of an engine's memory: the .NET garbage
collector sees none of it.

The same figures are published as metrics under the meter `Alethic.Node` (`NodeEnginePool.MeterName`), for
OpenTelemetry or any `MeterListener`: gauges for all of the above, counters for leases returned, acquisitions refused
by reason (`timeout`, `overload`, `cancelled`) and engines started and retired, and a histogram of how long served
acquisitions waited in line. Nothing is measured until a collector listens.

The pool logs its decisions at `Debug` under `Alethic.Node.NodeEnginePool`; the engines log each call at `Trace`.

## Constraints

- **One Node per process.** Node starts once per process and cannot start again, so every pool in a process shares
  one runtime platform. An ASP.NET application restarted in a new AppDomain of the same process cannot start Node
  again until the process is recycled.
- **CommonJS only.** The embedded runtime cannot `import()`, so a module is CommonJS, bundled with its dependencies
  and without code splitting (for esbuild, `--format=cjs` with splitting off).
- **Module scope is shared** by every call on an engine, since the module loads once. Per-call state belongs in the
  call, in an `AsyncLocalStorage` say, not at module scope.
- **A rebuilt module is not picked up** until the host restarts: Node holds it for the engine's life.
- **Cancelling an acquisition** abandons the wait. Work already running on an engine runs to completion.
