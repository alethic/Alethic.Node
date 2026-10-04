using Alethic.Node;

using Microsoft.JavaScript.NodeApi;

namespace Sample.Load;

/// <summary>
/// Callers calling the work module through the pool, as many at once as the pool lets through, each taking the
/// phase's calls in turn, until told to stop.
/// </summary>
public sealed class Workload
{

    readonly NodeEnginePool pool;
    readonly NodeModuleSource module;

    long completed;
    long refused;
    long failed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="module">The work module.</param>
    public Workload(NodeEnginePool pool, NodeModuleSource module)
    {
        this.pool = pool;
        this.module = module;
    }

    /// <summary>
    /// The calls completed so far.
    /// </summary>
    public long Completed => Interlocked.Read(ref completed);

    /// <summary>
    /// The calls refused so far, for want of capacity.
    /// </summary>
    public long Refused => Interlocked.Read(ref refused);

    /// <summary>
    /// The calls that failed so far for any other reason.
    /// </summary>
    public long Failed => Interlocked.Read(ref failed);

    /// <summary>
    /// Runs the callers until the token says stop.
    /// </summary>
    /// <param name="callers">How many.</param>
    /// <param name="calls">What they call, in turn.</param>
    /// <param name="stop">Stops them.</param>
    public Task RunAsync(int callers, IReadOnlyList<Call> calls, CancellationToken stop)
    {
        return Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(() => CallerAsync(i, calls, stop))));
    }

    /// <summary>
    /// One caller, calling until told to stop.
    /// </summary>
    /// <param name="index">Which caller, for which call it starts on.</param>
    /// <param name="calls">The calls, in turn.</param>
    /// <param name="stop">Stops it.</param>
    async Task CallerAsync(int index, IReadOnlyList<Call> calls, CancellationToken stop)
    {
        for (var n = index; stop.IsCancellationRequested == false; n++)
        {
            var call = calls[n % calls.Count];

            try
            {
                await pool.RunAsync(module, async exports =>
                {
                    var result = exports.CallMethod(call.Export, call.Argument);
                    return result.IsPromise() ? (bool)await ((JSPromise)result).AsTask() : (bool)result;
                });

                Interlocked.Increment(ref completed);
            }
            catch (TimeoutException)
            {
                // The pool had no capacity for it in time: what overload looks like to a caller.
                Interlocked.Increment(ref refused);
            }
            catch (Exception e) when (stop.IsCancellationRequested == false)
            {
                if (Interlocked.Increment(ref failed) == 1)
                    Console.Error.WriteLine($"A call failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

}
