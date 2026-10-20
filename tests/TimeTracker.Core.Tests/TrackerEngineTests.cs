namespace TimeTracker.Core.Tests;

/// <summary>Regression coverage for daily timing, state transitions, and lifecycle recovery.</summary>
public sealed class TrackerEngineTests
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Builds an empty engine with deterministic UTC daily boundaries.</summary>
    private TrackerEngine CreateEngine() => new(TrackerDocument.Empty, clock, TimeZoneInfo.Utc);

    [Fact]
    public void NewTrackerIsPausedAndTrimsReference()
    {
        var engine = CreateEngine();
        var id = engine.Add("  BUG-42  ", TrackerColors.All[1]);
        Assert.False(engine.IsRunning(id));
        Assert.Equal("BUG-42", Assert.Single(engine.Entries).Label);
        Assert.Equal(TimeSpan.Zero, engine.Entries[0].Elapsed);
    }

    [Fact]
    public void StartStopAndResumeAccumulateOnlyRunningTime()
    {
        var engine = CreateEngine();
        var id = engine.Add("42", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Stop(id);
        clock.Advance(TimeSpan.FromHours(1));
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(5));
        engine.Stop(id);
        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(engine.Entries).Elapsed);
    }

    [Fact]
    public void RepeatedStartAndRefreshDoNotDoubleCount()
    {
        var engine = CreateEngine();
        var id = engine.Add("42", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromSeconds(5));
        engine.Start(id);
        engine.Advance();
        engine.Advance();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), engine.Snapshot().Entries[0].Elapsed);
    }

    [Fact]
    public void MultipleTimersRemainIndependent()
    {
        var engine = CreateEngine();
        var first = engine.Add("A", TrackerColors.All[0]);
        var second = engine.Add("B", TrackerColors.All[1]);
        engine.Start(first);
        clock.Advance(TimeSpan.FromMinutes(2));
        engine.Start(second);
        clock.Advance(TimeSpan.FromMinutes(3));
        engine.Stop(first);
        clock.Advance(TimeSpan.FromMinutes(1));
        engine.Stop(second);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Entries[0].Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(4), engine.Entries[1].Elapsed);
    }

    [Fact]
    public void PauseAllKeepsTotalsAndNeverAutoResumes()
    {
        var engine = CreateEngine();
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromSeconds(12));
        engine.PauseAll();
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromSeconds(12), engine.Snapshot().Entries[0].Elapsed);
        Assert.False(engine.IsRunning(id));
    }

    [Fact]
    public void RecoveredSnapshotRetainsTotalButNotRunningState()
    {
        var engine = CreateEngine();
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromSeconds(12));
        var recovered = new TrackerEngine(engine.Snapshot(), clock, TimeZoneInfo.Utc);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromSeconds(12), recovered.Snapshot().Entries[0].Elapsed);
        Assert.False(recovered.IsRunning(id));
    }

    [Fact]
    public void EndFreezesTodayEntryUntilManualDeletion()
    {
        var engine = CreateEngine();
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(4));
        engine.End(id);
        Assert.Throws<InvalidOperationException>(() => engine.Start(id));
        Assert.True(engine.Entries[0].IsEnded);
        Assert.Equal(TimeSpan.FromMinutes(4), engine.Entries[0].Elapsed);
        engine.Delete(id);
        Assert.Empty(engine.Entries);
    }

    [Fact]
    public void ArchiveRequiresEndedEntryAndPreservesItAcrossRecovery()
    {
        var engine = CreateEngine();
        var id = engine.Add("ARCHIVE-1", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(8));
        Assert.Throws<InvalidOperationException>(() => engine.Archive(id));
        engine.End(id);
        var finished = Assert.Single(engine.Entries);
        engine.Archive(id);
        engine.Archive(id);
        var recovered = new TrackerEngine(engine.Snapshot(), clock, TimeZoneInfo.Utc);
        Assert.Equal(finished with { IsArchived = true }, Assert.Single(recovered.Entries));
        Assert.False(recovered.IsRunning(id));
        Assert.Throws<InvalidOperationException>(() => recovered.Start(id));
        clock.Advance(TimeSpan.FromDays(1));
        var today = recovered.Start(id);
        Assert.NotEqual(id, today);
        Assert.False(recovered.Entries.Single(entry => entry.Id == today).IsArchived);
    }

    [Fact]
    public void EditingRunningEntryPreservesTimeAndRunningState()
    {
        var engine = CreateEngine();
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(2));
        engine.Edit(id, "B", TrackerColors.All[1]);
        Assert.True(engine.IsRunning(id));
        Assert.Equal("B", engine.Entries[0].Label);
        Assert.Equal(TimeSpan.FromMinutes(2), engine.Snapshot().Entries[0].Elapsed);
    }

    [Fact]
    public void MidnightStopsAtBoundaryAndRequiresReviewEvenWhenTickIsLate()
    {
        var midnightClock = new ManualClock(new DateTimeOffset(2026, 10, 20, 23, 58, 0, TimeSpan.Zero));
        var engine = new TrackerEngine(TrackerDocument.Empty, midnightClock, TimeZoneInfo.Utc);
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        midnightClock.Advance(TimeSpan.FromHours(8));
        engine.Advance();
        var entry = Assert.Single(engine.Entries);
        Assert.Equal(TimeSpan.FromMinutes(2), entry.Elapsed);
        Assert.True(entry.NeedsReview);
        Assert.False(engine.IsRunning(id));
        engine.MarkReviewed(id);
        Assert.False(engine.Entries[0].NeedsReview);
    }

    [Fact]
    public void MidnightUsesLocalTimeZoneInsteadOfUtcDate()
    {
        var localZone = TimeZoneInfo.CreateCustomTimeZone("UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        var localClock = new ManualClock(new DateTimeOffset(2026, 10, 20, 18, 59, 0, TimeSpan.Zero));
        var engine = new TrackerEngine(TrackerDocument.Empty, localClock, localZone);
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        localClock.Advance(TimeSpan.FromMinutes(2));
        engine.Advance();
        Assert.Equal(TimeSpan.FromMinutes(1), engine.Entries[0].Elapsed);
        Assert.True(engine.Entries[0].NeedsReview);
        Assert.Equal(new DateOnly(2026, 10, 21), engine.Today);
    }

    [Fact]
    public void MidnightBoundaryAccountsForDaylightSavingFallBack()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var daylightClock = new ManualClock(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        var engine = new TrackerEngine(TrackerDocument.Empty, daylightClock, eastern);
        var id = engine.Add("DST-1", TrackerColors.All[0]);
        engine.Start(id);
        daylightClock.Advance(TimeSpan.FromHours(26));
        engine.Advance();
        Assert.Equal(TimeSpan.FromHours(25), engine.Entries[0].Elapsed);
        Assert.True(engine.Entries[0].NeedsReview);
        Assert.False(engine.IsRunning(id));
    }

    [Fact]
    public void StartingHistoricalEntryCreatesFreshTodayAndKeepsOldTotal()
    {
        var engine = CreateEngine();
        var previous = engine.Add("A", TrackerColors.All[0]);
        engine.Start(previous);
        clock.Advance(TimeSpan.FromMinutes(4));
        engine.End(previous);
        clock.Advance(TimeSpan.FromDays(1));
        var today = engine.Start(previous);
        Assert.NotEqual(previous, today);
        Assert.Equal(TimeSpan.FromMinutes(4), engine.Entries[0].Elapsed);
        Assert.Equal(TimeSpan.Zero, engine.Entries[1].Elapsed);
        engine.Stop(today);
        Assert.Equal(today, engine.Start(previous));
        Assert.Equal(2, engine.Entries.Count);
    }

    [Fact]
    public void DescriptionIsOptionalValidatedAndCarriedIntoTheNextDay()
    {
        var engine = CreateEngine();
        var id = engine.Add("NOTES-1", TrackerColors.All[0], "  Review acceptance criteria  ");
        Assert.Equal("Review acceptance criteria", Assert.Single(engine.Entries).Description);
        engine.Edit(id, "NOTES-1", TrackerColors.All[1], "Verify the fix");
        engine.End(id);
        engine.Archive(id);
        clock.Advance(TimeSpan.FromDays(1));
        var today = engine.Start(id);
        Assert.Equal("Verify the fix", engine.Entries.Single(entry => entry.Id == today).Description);
        Assert.Throws<ArgumentException>(() => engine.Add("A", TrackerColors.All[0], new string('x', 161)));
        Assert.Throws<ArgumentException>(() => engine.Edit(today, "A", TrackerColors.All[0], "two\nlines"));
        Assert.Throws<InvalidDataException>(() => TrackerEngine.Validate(new TrackerDocument(1,
            [engine.Entries[0] with { Description = null! }])));
        var noDescription = engine.Add("OPTIONAL", TrackerColors.All[0]);
        Assert.Equal("", engine.Entries.Single(entry => entry.Id == noDescription).Description);
    }

    [Fact]
    public void BackwardsClockDoesNotSubtractOrCountSameIntervalTwice()
    {
        var engine = CreateEngine();
        var id = engine.Add("A", TrackerColors.All[0]);
        engine.Start(id);
        clock.Advance(TimeSpan.FromMinutes(2));
        engine.Advance();
        clock.Advance(TimeSpan.FromMinutes(-1));
        engine.Advance();
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(3), engine.Snapshot().Entries[0].Elapsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyReferencesAreRejected(string label)
    {
        Assert.Throws<ArgumentException>(() => CreateEngine().Add(label, TrackerColors.All[0]));
    }

    [Fact]
    public void UnsupportedAccentAndOverlongLabelAreRejected()
    {
        var engine = CreateEngine();
        Assert.Throws<ArgumentException>(() => engine.Add("A", "invalid"));
        Assert.Throws<ArgumentException>(() => engine.Add(new string('A', 121), TrackerColors.All[0]));
    }

    [Fact]
    public void InvalidDocumentsCannotIntroduceDuplicateIdentifiersOrNegativeTime()
    {
        var entry = new TrackerEntry(Guid.NewGuid(), "A", TrackerColors.All[0], new DateOnly(2026, 10, 20), TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(() => new TrackerEngine(new TrackerDocument(1, [entry, entry])));
        Assert.Throws<InvalidDataException>(() => new TrackerEngine(new TrackerDocument(1, [entry with { Elapsed = TimeSpan.FromSeconds(-1) }])));
        Assert.Throws<InvalidDataException>(() => new TrackerEngine(new TrackerDocument(2, [])));
    }
}
