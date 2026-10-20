namespace TimeTracker.Core.Tests;

/// <summary>Advances time deterministically; timer tests never depend on sleeping or scheduling.</summary>
internal sealed class ManualClock(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset current = initial;

    /// <summary>Returns the explicitly controlled UTC instant.</summary>
    public override DateTimeOffset GetUtcNow() => current;

    /// <summary>Moves simulated wall time by the requested duration, including backwards jumps.</summary>
    public void Advance(TimeSpan duration) => current += duration;
}
