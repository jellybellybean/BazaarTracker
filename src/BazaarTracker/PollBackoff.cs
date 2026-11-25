namespace BazaarTracker;

/// <summary>
/// Decides how long to wait before the next poll. After each failure the delay grows
/// exponentially (with jitter) up to <see cref="MaxDelay"/>, and a Retry-After sent by the server
/// is honoured. A success resets it back to the normal polling interval.
/// </summary>
public sealed class PollBackoff
{
    /// <summary>Upper bound of the exponential backoff.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>Sanity limit for a Retry-After value sent by the server.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private readonly Random _random;

    public PollBackoff(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    /// <summary>Number of consecutive failed polls.</summary>
    public int Failures { get; private set; }

    public void Reset() => Failures = 0;

    /// <summary>
    /// Registers the outcome of a poll and returns the delay before the next one.
    /// </summary>
    /// <param name="interval">The normal polling interval from settings.</param>
    /// <param name="failed">true if the poll failed because of the network or the server.</param>
    /// <param name="retryAfter">Retry-After from the server, if it sent one.</param>
    public TimeSpan NextDelay(TimeSpan interval, bool failed, TimeSpan? retryAfter = null)
    {
        Failures = failed ? Failures + 1 : 0;

        var delay = interval;
        if (Failures > 0)
        {
            // interval * 2, 4, 8, ... capped; "equal jitter": half fixed, half random.
            var cap = interval > MaxDelay ? interval : MaxDelay;
            var factor = Math.Pow(2, Math.Min(Failures, 10));
            var exponential = TimeSpan.FromTicks((long)Math.Min(interval.Ticks * factor, cap.Ticks));
            var jittered = exponential / 2 + exponential / 2 * _random.NextDouble();
            delay = jittered > interval ? jittered : interval;
        }

        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            // honour the server's request: never come back earlier than it asked
            var requested = wait < MaxRetryAfter ? wait : MaxRetryAfter;
            requested += requested * (0.1 * _random.NextDouble());
            if (requested > delay)
            {
                delay = requested;
            }
        }

        return delay;
    }
}
