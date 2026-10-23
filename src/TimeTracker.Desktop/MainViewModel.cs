using System.Collections.ObjectModel;
using System.IO;
using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Coordinates timer state and persistence while leaving dialogs and Windows events to the view.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly TrackerEngine engine;
    private readonly JsonFileStore<TrackerDocument> trackerStore;
    private readonly JsonFileStore<UserPreferences> preferencesStore;
    private DateOnly displayedDate;
    private string message = "";
    private bool hasSaveError;

    /// <summary>Loads recoverable local state. Unreadable data stops startup rather than silently resetting totals.</summary>
    public MainViewModel(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        trackerStore = new(Path.Combine(dataDirectory, "tracker-data.json"), TrackerEngine.Validate);
        preferencesStore = new(Path.Combine(dataDirectory, "preferences.json"), ValidatePreferences);
        var loaded = trackerStore.Load(() => TrackerDocument.Empty);
        var preferences = preferencesStore.Load(() => new UserPreferences());
        engine = new TrackerEngine(loaded.Value);
        Preferences = preferences.Value;
        message = loaded.Warning ?? preferences.Warning ?? "";
        displayedDate = engine.Today;
        RebuildRows();
    }

    /// <summary>Gets today's rows, retained until explicitly deleted.</summary>
    public ObservableCollection<TrackerRowViewModel> TodayRows { get; } = [];
    /// <summary>Gets previous daily rows and explicitly archived entries, newest dates first.</summary>
    public ObservableCollection<TrackerRowViewModel> EarlierRows { get; } = [];
    /// <summary>Gets the local folder exposed through Settings.</summary>
    public string DataDirectory { get; }
    /// <summary>Gets the saved appearance and window geometry.</summary>
    public UserPreferences Preferences { get; private set; }
    /// <summary>Gets a user-visible storage or recovery message.</summary>
    public string Message { get => message; private set { message = value; Notify(); } }
    /// <summary>Gets the local date heading.</summary>
    public string DateHeading => engine.Today.ToString("dddd, MMMM d");
    /// <summary>Gets an explicit active-timer count, including overlapping timers.</summary>
    public string Summary => $"{engine.Entries.Count(entry => engine.IsRunning(entry.Id))} running · {TodayRows.Count} today";
    /// <summary>Indicates whether the initial empty-state hint should be visible.</summary>
    public bool IsEmpty => TodayRows.Count == 0;
    /// <summary>Gets the previous-entry section heading, including review warnings.</summary>
    public string HistoryHeading => $"Earlier days · {EarlierRows.Count} saved" + (EarlierRows.Any(row => row.NeedsReview) ? " · review needed" : "");
    /// <summary>Indicates whether any historical entry needs midnight review.</summary>
    public bool HasReviewWarnings => EarlierRows.Any(row => row.NeedsReview);

    /// <summary>Indicates the selected original card order in the inline sort selector.</summary>
    public bool IsAddedOrder => Preferences.SortOrder == TrackerSortOrder.Added;
    /// <summary>Indicates the selected ascending card order in the inline sort selector.</summary>
    public bool IsAscendingOrder => Preferences.SortOrder == TrackerSortOrder.Ascending;
    /// <summary>Indicates the selected descending card order in the inline sort selector.</summary>
    public bool IsDescendingOrder => Preferences.SortOrder == TrackerSortOrder.Descending;

    /// <summary>Persists the card-order preference and rearranges retained rows without changing timer state.</summary>
    public void SetSortOrder(TrackerSortOrder sortOrder)
    {
        if (!Enum.IsDefined(sortOrder)) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        // Restoring a checked binding must not recursively save or reorder the same selection.
        if (Preferences.SortOrder == sortOrder) return;
        SavePreferences(Preferences with { SortOrder = sortOrder });
        RebuildRows();
        Notify(nameof(IsAddedOrder));
        Notify(nameof(IsAscendingOrder));
        Notify(nameof(IsDescendingOrder));
    }

    /// <summary>Adds a new paused card using the editor's validated inputs.</summary>
    public void Add(string label, string color, string description = "") => Change(() => engine.Add(label, color, description));
    /// <summary>Updates a row's descriptive fields without changing its time.</summary>
    public void Edit(Guid id, string label, string color, string description = "") => Change(() => engine.Edit(id, label, color, description));
    /// <summary>Toggles one timer without stopping other running cards.</summary>
    public void Toggle(Guid id) => Change(() => { if (engine.IsRunning(id)) engine.Stop(id); else engine.Start(id); });
    /// <summary>Finalizes the current total while retaining the entry.</summary>
    public void End(Guid id) => Change(() => engine.End(id));
    /// <summary>Retains a finished entry in Earlier days rather than Today's list.</summary>
    public void Archive(Guid id) => Change(() => engine.Archive(id));
    /// <summary>Returns a retained card to today's list while preserving historical totals and any current session.</summary>
    public void Unarchive(Guid id) => Change(() => engine.Unarchive(id));
    /// <summary>Deletes only the requested row after UI confirmation.</summary>
    public void Delete(Guid id) => Change(() => engine.Delete(id));
    /// <summary>Clears the midnight review marker without altering recorded time.</summary>
    public void MarkReviewed(Guid id) => Change(() => engine.MarkReviewed(id));
    /// <summary>Pauses and immediately saves all timers for a Windows lifecycle event.</summary>
    public bool PauseAll()
    {
        engine.PauseAll();
        Refresh();
        return Save();
    }

    /// <summary>Updates elapsed displays and moves midnight-paused entries into history.</summary>
    public void Refresh()
    {
        engine.Advance();
        if (displayedDate != engine.Today)
        {
            displayedDate = engine.Today;
            RebuildRows();
        }
        else
        {
            var rows = TodayRows.Concat(EarlierRows).ToDictionary(row => row.Id);
            foreach (var entry in engine.Entries)
                if (rows.TryGetValue(entry.Id, out var row)) row.Refresh(entry, engine.IsRunning(entry.Id), entry.Date != engine.Today);
        }
        Notify(nameof(Summary));
        Notify(nameof(DateHeading));
        Notify(nameof(HistoryHeading));
        Notify(nameof(HasReviewWarnings));
    }

    /// <summary>Returns current clipboard output, sampling time before formatting.</summary>
    public string CopyTotal(Guid id, bool minutesOnly)
    {
        var entry = engine.Snapshot().Entries.Single(entry => entry.Id == id);
        return minutesOnly ? TimeDisplay.Minutes(entry.Elapsed) : TimeDisplay.HoursAndMinutes(entry.Elapsed);
    }

    /// <summary>Saves current totals; a visible error remains until a later successful retry.</summary>
    public bool Save()
    {
        try
        {
            trackerStore.Save(engine.Snapshot());
            if (hasSaveError) { Message = ""; hasSaveError = false; }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hasSaveError = true;
            Message = $"Could not save your time: {exception.Message} Free space or check folder permissions; Time Tracker will retry.";
            return false;
        }
    }

    /// <summary>Saves theme and placement independently from tracker data.</summary>
    public void SavePreferences(UserPreferences preferences)
    {
        Preferences = preferences;
        try { preferencesStore.Save(preferences); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Message = $"Could not save preferences: {exception.Message}"; }
    }

    /// <summary>Applies one action, immediately persists it, and refreshes the visible row lists.</summary>
    private void Change(Action change)
    {
        change();
        RebuildRows();
        Save();
    }

    /// <summary>Synchronizes daily collections while retaining existing row objects and keyboard focus.</summary>
    private void RebuildRows()
    {
        SynchronizeRows(TodayRows, historical: false);
        SynchronizeRows(EarlierRows, historical: true);
        Notify(nameof(IsEmpty));
        Notify(nameof(Summary));
        Notify(nameof(HistoryHeading));
        Notify(nameof(HasReviewWarnings));
    }

    /// <summary>Applies one shared collection-update algorithm to today's entries and earlier days.</summary>
    private void SynchronizeRows(ObservableCollection<TrackerRowViewModel> rows, bool historical)
    {
        var desired = TrackerOrdering.Order(
            engine.Entries.Where(entry => (entry.Date != engine.Today || entry.IsArchived) == historical),
            Preferences.SortOrder).ToArray();
        var identifiers = desired.Select(entry => entry.Id).ToHashSet();
        foreach (var row in rows.Where(row => !identifiers.Contains(row.Id)).ToArray()) rows.Remove(row);
        for (var index = 0; index < desired.Length; index++)
        {
            var entry = desired[index];
            var existing = rows.FirstOrDefault(row => row.Id == entry.Id);
            if (existing is null) rows.Insert(index, new TrackerRowViewModel(entry, engine.IsRunning(entry.Id), entry.Date != engine.Today));
            else
            {
                existing.Refresh(entry, engine.IsRunning(entry.Id), entry.Date != engine.Today);
                if (rows.IndexOf(existing) != index) rows.Move(rows.IndexOf(existing), index);
            }
        }
    }

    /// <summary>Rejects malformed preference files before assigning WPF window dimensions.</summary>
    private static void ValidatePreferences(UserPreferences preferences)
    {
        if (!Enum.IsDefined(preferences.SortOrder) || preferences.Theme is not ("System" or "Light" or "Dark") ||
            !double.IsFinite(preferences.Width) || !double.IsFinite(preferences.Height) ||
            preferences.Width < 520 || preferences.Height < 260 ||
            preferences.Left is double left && !double.IsFinite(left) ||
            preferences.Top is double top && !double.IsFinite(top))
            throw new InvalidDataException("Invalid theme, sort order, or window size in preferences.json.");
    }
}
