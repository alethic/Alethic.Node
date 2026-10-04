# Sample.Load

An adaptive `NodeEnginePool` under a workload that changes shape, watched as it learns. A console application, no web
anywhere: it runs for about a minute, prints a line a second of what the pool has decided, lets the pool's own log say
why, and at the end of each phase checks what the pool should have done. It exits 1 where the pool did not.

```shell
dotnet run --project samples/Sample.Load -c Release
```

The pool runs up to half the machine's processors in engines, at most four, each from 1 to 32 leases, with the default
40 ms event-loop target, a ten-second idle timeout, and overload detection after two seconds of a standing queue.

| Phase | Work | What the pool should do |
| --- | --- | --- |
| idle | nothing, 3 s | One engine, no delay. |
| waiting | 48 callers of 30 ms waits | Keep the loop under the target and the limit high: waiting work costs the thread nothing. |
| computing | 32 callers of 20 ms computes | See the loop fall behind, bring the limit down, and try more engines, which pay since each has a core. |
| mixed | 32 callers, half and half | Settle somewhere between. |
| overload | 128 callers of 50 ms computes | Count itself overloaded after two seconds of a standing queue, and refuse callers fast rather than serve them late. |
| allocating | each engine keeps 64 MB | Show it in the engine's heap. |
| releasing | each engine lets it go | |
| quiet | nothing, 16 s | Retire the idle engines back to one. |

Each line gives, per engine, its id, leases held over its limit, its event-loop delay and its heap in use; and for the
pool, the calls completed that second, the acquisitions waiting, whether it is overloaded, the calls refused so far in
the phase, and the memory load. The counters at the end are read off the pool's meter, as a collector would read them.

The work is clocked rather than computed, a spin until the wall clock has moved and a timer, so a faster machine does
not finish it sooner: what the pool learns from is how the engine's thread is occupied.
