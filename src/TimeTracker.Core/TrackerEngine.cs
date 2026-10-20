namespace TimeTracker.Core;

/// <summary>Owns daily timing rules independently of UI threads, Windows, and disk storage.</summary>
public sealed class TrackerEngine
{
    private readonly TimeProvider clock;
    private readonly TimeZoneInfo timeZone;
    private readonly List<TrackerEntry> entries;
    private readonly Dictionary<Guid, DateTimeOffset> runningSince = [];

    /// <summary>Creates paused trackers from saved data using an injectable clock and time zone.</summary>
    public TrackerEngine(TrackerDocument document, TimeProvider? clock = null, TimeZoneInfo? timeZone = null)
    {
        Validate(document);
        entries = [.. document.Entries];
        this.clock = clock ?? TimeProvider.System;
        this.timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    /// <summary>Returns a snapshot so callers cannot mutate internal timing state.</summary>
    public IReadOnlyList<TrackerEntry> Entries => entries.ToArray();

    /// <summary>Gets the current local calendar date used for daily entries.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone).DateTime);

    /// <summary>Indicates whether an entry currently accumulates time.</summary>
    public bool IsRunning(Guid id) => runningSince.ContainsKey(id);

    /// <summary>Adds a paused entry for today; the label is a card reference, not a required integration.</summary>
    public Guid Add(string label, string color)
    {
        ValidateLabelAndColor(label, color);
        var entry = new TrackerEntry(Guid.NewGuid(), label.Trim(), color, Today, TimeSpan.Zero);
        entries.Add(entry);
        return entry.Id;
    }

    /// <summary>Changes only the label and accent, preserving all recorded time.</summary>
    public void Edit(Guid id, string label, string color)
    {
        ValidateLabelAndColor(label, color);
        var index = FindIndex(id);
        entries[index] = entries[index] with { Label = label.Trim(), Color = color };
    }

    /// <summary>Starts a paused entry. Older days create a fresh daily entry, preserving history.</summary>
    /// <returns>The entry now running, which can differ from the historical entry requested.</returns>
    public Guid Start(Guid id)
    {
        Advance();
        var entry = entries[FindIndex(id)];
        if (entry.Date != Today)
        {
            // Reuse today's unfinished entry when returning to the same historical card.
            var todayEntry = entries.LastOrDefault(candidate => candidate.Date == Today &&
                candidate.Label == entry.Label && !candidate.IsEnded);
            id = todayEntry?.Id ?? Add(entry.Label, entry.Color);
            entry = entries[FindIndex(id)];
        }
        if (entry.IsEnded)
            throw new InvalidOperationException("This entry has ended. Create a new tracker to record more time.");
        runningSince.TryAdd(id, clock.GetUtcNow());
        return id;
    }

    /// <summary>Pauses an entry while retaining its accumulated total.</summary>
    public void Stop(Guid id)
    {
        Advance();
        runningSince.Remove(id);
    }

    /// <summary>Pauses all entries on lock, suspend, or normal application exit.</summary>
    public void PauseAll()
    {
        Advance();
        runningSince.Clear();
    }

    /// <summary>Finalizes an entry; its total remains visible and copyable.</summary>
    public void End(Guid id)
    {
        Stop(id);
        var index = FindIndex(id);
        entries[index] = entries[index] with { IsEnded = true };
    }

    /// <summary>Dismisses the midnight warning after the user reviews a potentially forgotten timer.</summary>
    public void MarkReviewed(Guid id)
    {
        var index = FindIndex(id);
        entries[index] = entries[index] with { NeedsReview = false };
    }

    /// <summary>Removes an entry after the interface has obtained deletion confirmation.</summary>
    public void Delete(Guid id)
    {
        runningSince.Remove(id);
        entries.RemoveAt(FindIndex(id));
    }

    /// <summary>Accumulates elapsed time and stops older entries exactly at their local midnight.</summary>
    public void Advance()
    {
        var now = clock.GetUtcNow();
        foreach (var (id, started) in runningSince.ToArray())
        {
            var index = FindIndex(id);
            var entry = entries[index];
            var nextDay = entry.Date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // Some zones skip midnight during DST. Use the first valid local minute of the new day.
            while (timeZone.IsInvalidTime(nextDay)) nextDay = nextDay.AddMinutes(1);
            var midnight = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextDay, timeZone));
            var through = now < midnight ? now : midnight;
            var increment = through > started ? through - started : TimeSpan.Zero;
            entries[index] = entry with
            {
                Elapsed = entry.Elapsed + increment,
                NeedsReview = entry.NeedsReview || now >= midnight
            };
            if (now >= midnight) runningSince.Remove(id);
            // Do not count a backwards clock jump twice when wall time catches up.
            else if (now > started) runningSince[id] = now;
        }
    }

    /// <summary>Captures current totals. Running state never enters the saved file.</summary>
    public TrackerDocument Snapshot()
    {
        Advance();
        return new TrackerDocument(1, Entries);
    }

    /// <summary>Rejects unsupported or malformed files before they can replace good saved data.</summary>
    public static void Validate(TrackerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != 1 || document.Entries is null)
            throw new InvalidDataException("Unsupported tracker file format.");
        var identifiers = new HashSet<Guid>();
        foreach (var entry in document.Entries)
        {
            if (entry is null || entry.Id == Guid.Empty || !identifiers.Add(entry.Id) || entry.Elapsed < TimeSpan.Zero || entry.Date == DateOnly.MaxValue)
                throw new InvalidDataException("The tracker file contains an invalid entry.");
            try { ValidateLabelAndColor(entry.Label, entry.Color); }
            catch (ArgumentException exception) { throw new InvalidDataException("The tracker file contains an invalid label or color.", exception); }
        }
    }

    /// <summary>Finds a tracker or fails explicitly rather than editing the wrong row.</summary>
    private int FindIndex(Guid id)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException("Tracker no longer exists.");
    }

    /// <summary>Enforces the same input rules for newly created and edited entries.</summary>
    private static void ValidateLabelAndColor(string label, string color)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > 120)
            throw new ArgumentException("Enter a card reference between 1 and 120 characters.", nameof(label));
        if (!TrackerColors.All.Contains(color))
            throw new ArgumentException("Choose one of the available accent colors.", nameof(color));
    }
}
