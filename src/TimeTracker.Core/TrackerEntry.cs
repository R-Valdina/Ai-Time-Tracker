namespace TimeTracker.Core;

/// <summary>A single day's card tracker. Running state is deliberately transient.</summary>
public sealed record TrackerEntry(
    Guid Id,
    string Label,
    string Color,
    DateOnly Date,
    TimeSpan Elapsed,
    bool IsEnded = false,
    bool NeedsReview = false,
    bool IsArchived = false,
    string Description = "");

/// <summary>Versioned on-disk data. No running session can resume accidentally after a crash.</summary>
public sealed record TrackerDocument(int Version, IReadOnlyList<TrackerEntry> Entries)
{
    /// <summary>Creates an empty document using the supported file format.</summary>
    public static TrackerDocument Empty => new(1, []);
}

/// <summary>Persistent appearance, card ordering, and normal (non-maximized) window geometry.</summary>
public sealed record UserPreferences(
    string Theme = "System",
    double Width = 580,
    double Height = 400,
    double? Left = null,
    double? Top = null,
    bool Maximized = false,
    bool ManuallySized = false,
    TrackerSortOrder SortOrder = TrackerSortOrder.Added);

/// <summary>Shared color choices used by both the editor and validation.</summary>
public static class TrackerColors
{
    /// <summary>Blue, red, purple, green, orange, and teal accents.</summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { "#2563EB", "#DC2626", "#9333EA", "#16A34A", "#EA580C", "#0D9488" });
}
