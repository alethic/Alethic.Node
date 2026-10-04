namespace Sample.Load;

/// <summary>
/// One stretch of the run: a workload of a shape, for a while, and what the pool should have done with it.
/// </summary>
/// <param name="Name">What to call it.</param>
/// <param name="Duration">How long it runs.</param>
/// <param name="Callers">How many callers call at once; none for a quiet stretch.</param>
/// <param name="Calls">The calls they make, each taking them in turn.</param>
/// <param name="Expect">What the pool should have done, judged from what was seen.</param>
public sealed record Phase(string Name, TimeSpan Duration, int Callers, IReadOnlyList<Call> Calls, Func<Observation, IEnumerable<(string What, bool Ok)>> Expect);
