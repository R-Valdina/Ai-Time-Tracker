using System.Globalization;
using System.Text.Json.Serialization;

namespace TimeTracker.Core;

/// <summary>Defines the user's preferred card-reference order within each local date.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrackerSortOrder>))]
public enum TrackerSortOrder
{
    /// <summary>Retains the legacy insertion-order value so older preference files remain readable.</summary>
    Added,
    /// <summary>Sorts references from lowest to highest using natural numeric ordering.</summary>
    Ascending,
    /// <summary>Sorts references from highest to lowest using natural numeric ordering.</summary>
    Descending
}

/// <summary>Shares stable, case-insensitive card ordering between current entries and retained history.</summary>
public static class TrackerOrdering
{
    private static readonly StringComparer CardReferences = StringComparer.Create(CultureInfo.InvariantCulture,
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>Keeps newer dates first and applies the selected card order within each date without changing records.</summary>
    public static IOrderedEnumerable<TrackerEntry> Order(IEnumerable<TrackerEntry> entries, TrackerSortOrder sortOrder)
    {
        var dates = entries.OrderByDescending(entry => entry.Date);
        return sortOrder switch
        {
            TrackerSortOrder.Added => dates,
            TrackerSortOrder.Ascending => dates.ThenBy(entry => entry.Label, CardReferences),
            TrackerSortOrder.Descending => dates.ThenByDescending(entry => entry.Label, CardReferences),
            _ => throw new ArgumentOutOfRangeException(nameof(sortOrder))
        };
    }
}
