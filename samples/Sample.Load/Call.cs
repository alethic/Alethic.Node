namespace Sample.Load;

/// <summary>
/// One kind of call the workload makes: an export of the work module, and what it is given.
/// </summary>
/// <param name="Export">The export: compute, fetch, allocate or release.</param>
/// <param name="Argument">What it is given: milliseconds, or megabytes.</param>
public sealed record Call(string Export, int Argument);
