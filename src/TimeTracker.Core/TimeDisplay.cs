using System.Globalization;

namespace TimeTracker.Core;

/// <summary>Formats durations consistently without rounding time up.</summary>
public static class TimeDisplay
{
    /// <summary>Returns total hours (including values over 24), minutes, and seconds.</summary>
    public static string Clock(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");

    /// <summary>Returns whole elapsed minutes for the secondary display and clipboard.</summary>
    public static string Minutes(TimeSpan elapsed) =>
        $"{((long)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture)} min";

    /// <summary>Returns whole hours and remaining minutes for manual timecard entry.</summary>
    public static string HoursAndMinutes(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)elapsed.TotalHours} h {elapsed.Minutes} min");
}
