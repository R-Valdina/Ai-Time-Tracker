namespace TimeTracker.Core.Tests;

/// <summary>Verifies human-readable totals used for manual timecard entry.</summary>
public sealed class TimeDisplayTests
{
    [Theory]
    [InlineData(0, "00:00:00", "0 min", "0 h 0 min")]
    [InlineData(59, "00:00:59", "0 min", "0 h 0 min")]
    [InlineData(5076, "01:24:36", "84 min", "1 h 24 min")]
    [InlineData(90000, "25:00:00", "1500 min", "25 h 0 min")]
    public void FormatsTotalHoursAndFloorsPartialMinutes(int seconds, string clock, string minutes, string hours)
    {
        var elapsed = TimeSpan.FromSeconds(seconds);
        Assert.Equal(clock, TimeDisplay.Clock(elapsed));
        Assert.Equal(minutes, TimeDisplay.Minutes(elapsed));
        Assert.Equal(hours, TimeDisplay.HoursAndMinutes(elapsed));
    }
}
