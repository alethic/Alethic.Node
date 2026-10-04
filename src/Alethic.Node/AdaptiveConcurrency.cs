using System;

namespace Alethic.Node;

/// <summary>
/// Learns how many leases one engine should hold at once, from its event-loop delay.
/// </summary>
/// <remarks>
/// A gradient limit, as adaptive concurrency limiters for services use, with the event loop's delay as the measure of
/// congestion in place of a request's latency: a lease spends much of its time waiting on things that leave the
/// engine's thread free, so its latency says little about whether the thread is saturated, and the loop's delay says
/// exactly that.
///
/// Over the target, the limit falls by the ratio of the target to the delay, by no more than half at a time and by at
/// least one. Under it, the limit rises, by the square root of the limit and at least one, so it climbs quickly while
/// small and carefully when large; but only where the limit was reached, since a limit nothing pressed against says
/// nothing about whether more would be welcome.
/// </remarks>
static class AdaptiveConcurrency
{

    /// <summary>
    /// The next limit for an engine.
    /// </summary>
    /// <param name="limit">Its limit now.</param>
    /// <param name="delay">Its event-loop delay over the window just ended, in milliseconds.</param>
    /// <param name="peak">The most leases it held at once over that window.</param>
    /// <param name="min">The least the limit may be.</param>
    /// <param name="max">The most the limit may be.</param>
    /// <param name="target">The event-loop delay to keep under, in milliseconds.</param>
    public static int Next(int limit, double delay, int peak, int min, int max, double target)
    {
        if (delay > target)
        {
            var scaled = (int)Math.Floor(limit * Math.Max(0.5, target / delay));
            return Clamp(Math.Min(limit - 1, scaled), min, max);
        }

        if (peak >= limit)
            return Clamp(limit + Math.Max(1, (int)Math.Sqrt(limit)), min, max);

        return Clamp(limit, min, max);
    }

    /// <summary>
    /// A value held within bounds.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The least it may be.</param>
    /// <param name="max">The most it may be.</param>
    static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

}
