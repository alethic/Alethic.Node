# Sample.Load

An adaptive `NodeEnginePool` under a workload that changes shape, watched as it learns. A console application: a
minute of phases, a line a second of what the pool decided, the pool's own log of why, and a check per phase of what
it should have done. Exits 1 where it did not.

```shell
dotnet run --project samples/Sample.Load -c Release
```

| Phase | Work | Expected |
| --- | --- | --- |
| waiting | 48 callers × 30 ms waits | Limits stay high; the loop keeps up. |
| computing | 32 callers × 20 ms computes | Limits fall; engines are added and kept. |
| overload | 128 callers × 50 ms computes | Overloaded; callers refused fast. |
| allocating | each engine keeps 64 MB | Seen in the heap. |
| quiet | nothing, 16 s | Idle engines retired. |

The work is clocked, a spin until the wall clock has moved and a timer, so a faster machine does not finish it sooner.
