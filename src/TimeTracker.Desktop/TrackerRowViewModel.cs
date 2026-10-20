using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Presents one tracker using the shared row template.</summary>
public sealed class TrackerRowViewModel(TrackerEntry entry, bool running, bool historical) : ObservableObject
{
    private TrackerEntry entry = entry;
    private bool running = running;

    /// <summary>Gets the stable identifier used by row commands.</summary>
    public Guid Id => entry.Id;
    /// <summary>Gets the card reference entered by the user.</summary>
    public string Label => entry.Label;
    /// <summary>Gets the selected left-border accent.</summary>
    public string Color => entry.Color;
    /// <summary>Gets the live duration in hours, minutes, and seconds.</summary>
    public string Total => TimeDisplay.Clock(entry.Elapsed);
    /// <summary>Gets the total whole minutes without rounding up.</summary>
    public string TotalMinutes => TimeDisplay.Minutes(entry.Elapsed);
    /// <summary>Gets a screen-reader-friendly duration label.</summary>
    public string AccessibleTotal => $"{Label}: {Total}";
    /// <summary>Indicates that this row belongs to a previous local date.</summary>
    public bool IsHistorical => historical;
    /// <summary>Indicates whether the row is actively accumulating time.</summary>
    public bool IsRunning => running;
    /// <summary>Gets the single toggle button's action text.</summary>
    public string ToggleText => historical ? "Start today" : entry.IsEnded ? "Ended" : running ? "■ Stop" : "▶ Start";
    /// <summary>Allows older ended rows to start a new day, but prevents reopening today's ended total.</summary>
    public bool CanToggle => historical || !entry.IsEnded;
    /// <summary>Prevents repeatedly ending an already-finalized row.</summary>
    public bool CanEnd => !entry.IsEnded;
    /// <summary>Indicates that a midnight pause still needs acknowledgement.</summary>
    public bool NeedsReview => entry.NeedsReview;
    /// <summary>Explains the date, ended state, or active state without depending on color.</summary>
    public string Status => historical ? $"{entry.Date:ddd, MMM d, yyyy}" : entry.IsEnded ? "Ended" : running ? "● Running" : "Paused";

    /// <summary>Refreshes live values while keeping the same row and keyboard focus.</summary>
    public void Refresh(TrackerEntry updated, bool isRunning)
    {
        entry = updated;
        running = isRunning;
        foreach (var name in new[] { nameof(Label), nameof(Color), nameof(Total), nameof(TotalMinutes), nameof(AccessibleTotal), nameof(IsRunning), nameof(ToggleText), nameof(CanToggle), nameof(CanEnd), nameof(NeedsReview), nameof(Status) })
            Notify(name);
    }
}
