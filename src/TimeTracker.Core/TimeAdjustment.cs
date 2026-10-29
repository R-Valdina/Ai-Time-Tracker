using System.Globalization;

namespace TimeTracker.Core;

/// <summary>Parses positive whole-minute corrections independently of Windows and the current locale.</summary>
public static class TimeAdjustment
{
    /// <summary>Accepts total minutes or hours:minutes, requiring a minute component from 00 through 59.</summary>
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length is < 1 or > 2) return false;
        if (!long.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first)) return false;
        long minutes = 0;
        if (parts.Length == 2 && (!long.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes > 59)) return false;
        try
        {
            var totalMinutes = parts.Length == 1 ? first : checked(first * 60 + minutes);
            duration = TimeSpan.FromTicks(checked(totalMinutes * TimeSpan.TicksPerMinute));
            return duration > TimeSpan.Zero;
        }
        catch (OverflowException) { return false; }
    }
}
