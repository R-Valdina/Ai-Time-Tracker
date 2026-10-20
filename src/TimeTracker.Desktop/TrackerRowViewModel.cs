using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Presents one tracker using the shared row template.</summary>
public sealed class TrackerRowViewModel(TrackerEntry entry, bool running, bool historical) : ObservableObject
{
    private TrackerEntry entry = entry;
    private bool running = running;
    private bool historical = historical;

    /// <summary>Gets the stable identifier used by row commands.</summary>
    public Guid Id => entry.Id;
    /// <summary>Gets the card reference entered by the user.</summary>
    public string Label => entry.Label;
    /// <summary>Gets the optional short note displayed along the bottom of the tracker.</summary>
    public string Description => entry.Description;
    /// <summary>Gets the selected left-border accent.</summary>
    public string Color => entry.Color;
    /// <summary>Gets the live duration in hours, minutes, and seconds.</summary>
    public string Total => TimeDisplay.Clock(entry.Elapsed);
    /// <summary>Gets the total whole minutes without rounding up.</summary>
    public string TotalMinutes => TimeDisplay.Minutes(entry.Elapsed);
    /// <summary>Gets a screen-reader-friendly duration label.</summary>
    public string AccessibleTotal => $"{Label}: {Total}";
    /// <summary>Names the card and action for Narrator without speaking the decorative play/stop symbols.</summary>
    public string AccessibleToggleName => $"{ToggleAction} timer for {Label}";
    /// <summary>Distinguishes identical-looking row menus when navigating with a screen reader.</summary>
    public string AccessibleActionsName => $"Actions for {Label}";
    /// <summary>Indicates that this row belongs to a previous local date.</summary>
    public bool IsHistorical => historical;
    /// <summary>Indicates whether the row is actively accumulating time.</summary>
    public bool IsRunning => running;
    /// <summary>Gets the single toggle button's action text.</summary>
    public string ToggleText => historical || entry.IsEnded ? ToggleAction : $"{(running ? "■" : "▶")} {ToggleAction}";
    /// <summary>Shares the action wording between visual labels and accessibility names.</summary>
    private string ToggleAction => historical ? "Start today" : entry.IsEnded ? "Ended" : running ? "Stop" : "Start";
    /// <summary>Allows older ended rows to start a new day, but prevents reopening today's ended total.</summary>
    public bool CanToggle => historical || !entry.IsEnded;
    /// <summary>Prevents repeatedly ending an already-finalized row.</summary>
    public bool CanEnd => !entry.IsEnded;
    /// <summary>Allows finished entries to move into history once.</summary>
    public bool CanArchive => entry.IsEnded && !entry.IsArchived;
    /// <summary>Indicates whether to display the finalized timecard summary on this row.</summary>
    public bool IsEnded => entry.IsEnded;
    /// <summary>Gets both final timecard formats directly on the retained entry.</summary>
    public string FinalTotal => $"Total: {TimeDisplay.HoursAndMinutes(entry.Elapsed)} · {TimeDisplay.Minutes(entry.Elapsed)}";
    /// <summary>Indicates that a midnight pause still needs acknowledgement.</summary>
    public bool NeedsReview => entry.NeedsReview;
    /// <summary>Explains the date, ended state, or active state without depending on color.</summary>
    public string Status => historical ? $"{entry.Date:ddd, MMM d, yyyy}" : entry.IsArchived ? $"Archived · {entry.Date:MMM d, yyyy}" : entry.IsEnded ? "Ended" : running ? "● Running" : "Paused";

    /// <summary>Refreshes live values while keeping the same row and keyboard focus.</summary>
    public void Refresh(TrackerEntry updated, bool isRunning, bool? isHistorical = null)
    {
        entry = updated;
        running = isRunning;
        historical = isHistorical ?? historical;
        foreach (var name in new[] { nameof(Label), nameof(Description), nameof(Color), nameof(Total), nameof(TotalMinutes), nameof(AccessibleTotal), nameof(AccessibleToggleName), nameof(AccessibleActionsName), nameof(IsRunning), nameof(IsHistorical), nameof(ToggleText), nameof(CanToggle), nameof(CanEnd), nameof(CanArchive), nameof(IsEnded), nameof(FinalTotal), nameof(NeedsReview), nameof(Status) })
            Notify(name);
    }
}
