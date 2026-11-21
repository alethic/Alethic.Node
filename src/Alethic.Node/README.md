# Alethic.Node

Node.js embedded in a .NET process: a pool of real Node runtimes (libnode, through
[node-api-dotnet](https://github.com/microsoft/node-api-dotnet)), each on its own thread, running JavaScript modules
inside your application. .NET 8 and 9, and .NET Framework 4.7.2 and later.

```shell
dotnet add package Alethic.Node
dotnet add package Microsoft.JavaScript.LibNode.win-x64    # the native runtime: one per platform you deploy to
```

The native runtime is LibNode 26 (Node.js 26), `Microsoft.JavaScript.LibNode.<rid>` 26.1.2 or later with
node-api-dotnet 0.9.44 or later: 64-bit only (x64, arm64; there is no x86 build), Windows with the Universal C Runtime,
Linux with glibc 2.35 or later (not Alpine or other musl distributions), macOS 13.5 or later. An older libnode, such
as 20.1800, ends the process when started, so the build refuses one (error ALNODE002), and on Windows the pool
refuses it again at run time, with an exception naming the file.

To take every platform's runtime with one reference instead, add `Microsoft.JavaScript.NodeApi.Embedding` (0.9.44 or
later) in place of the LibNode packages. It carries all six, x64 and arm64 on Windows, Linux and macOS: about 280 MB
of packages, and over 800 MB in the output of any build without a runtime identifier.

## Use

```csharp
services.AddNodeEnginePool(o =>
{
    o.EngineCount = 2;                 // CPU parallelism: the cores the process may use
    o.MaxConcurrencyPerEngine = 4;     // calls an engine runs at once
});

var pool = provider.GetRequiredService<NodeEnginePool>();
var module = NodeModuleSource.FromFile("tools.cjs");

var slug = await pool.RunAsync(module, exports =>
    Task.FromResult((string)exports.CallMethod("slugify", "Enchanted Rock")));

var digest = await pool.RunAsync(module, async exports =>
    (string)await ((JSPromise)exports.CallMethod("digest", "enchanted rock")).AsTask());
```

The callback runs on the engine's thread with the module's exports, in node-api-dotnet's types. A `JSValue` lives
only until the next `await`; hold one across it through a `JSReference`, and return plain .NET data.

A module is loaded once per engine with Node's `require` and keeps its state for the engine's life. It may be
CommonJS or an ES module; an ES module that awaits at its top level is imported instead, by `RunAsync` (the
synchronous `Run` cannot wait for it). A module can `import()` others, so a bundle may be split into chunks. Calls that must
share one engine take a lease: `await using var lease = await pool.AcquireAsync();` and run against it. Without a
container, construct the pool from its options, a logger factory and a service provider.

## Options

| | Default | |
| --- | --- | --- |
| `EngineCount` | 1 | Engines; adapting, the most. Inside a container, the processor count reports the host's cores. |
| `MaxConcurrencyPerEngine` | 4 | Calls an engine runs at once; adapting, the most. |
| `AcquireTimeout` | 10 s | How long a call waits for capacity. |
| `ConfigureEngine` | | Runs once on each engine as it starts. |
| `LibNodePath` | | The native runtime, where it is not under `runtimes/<rid>/native`. |

**Adaptive mode** (`Mode = NodeEnginePoolMode.Adaptive`) learns the limits within `MinEngineCount`..`EngineCount`
and `MinConcurrencyPerEngine`..`MaxConcurrencyPerEngine`, once a second:
- an engine's limit falls when work waits more than `TargetEventLoopDelay` (40 ms) for its thread, and rises when
  the limit is what holds work back;
- an engine is added when calls queue, kept if throughput rose, and retired after `EngineIdleTimeout` (30 s) idle;
- none is added above `MemoryLoadLimit` (0.9) of the machine's or container's memory.

**Overload**: set `OverloadInterval` to refuse calls fast once the queue has stood that long, instead of serving them
late.

## Watching it

`GetStatistics()` reports each engine's calls, limit, event-loop delay and heap, the queue, and the memory load. The
same figures are metrics under the meter `Alethic.Node`, with counters for calls, refusals and engines. The pool logs
its decisions at `Debug`.

## Constraints

- Node starts once per process. An ASP.NET application restarted in the same process cannot start it again.
- A module is a file: its own `import()` and `require` resolve from it, by Node's rules.
- The process is 64-bit: LibNode has no x86 build. A 32-bit process is refused unless `LibNodePath` names a library.
- Module state is shared by every call on an engine, and a rebuilt module is not picked up until restart.
- An engine's memory is V8's; the .NET garbage collector does not see it.
