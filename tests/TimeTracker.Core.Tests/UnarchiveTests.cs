namespace TimeTracker.Core.Tests;

/// <summary>Protects archived totals while restoring paused cards and reusing existing work safely.</summary>
public sealed class UnarchiveTests
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void SameDayUnarchiveCreatesZeroPausedEntryAndPreservesArchivedTotal()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty, clock, TimeZoneInfo.Utc);
        var originalId = engine.Add("BUG-42", TrackerColors.All[1], "Verify the login fix");
        engine.Start(originalId);
        clock.Advance(TimeSpan.FromMinutes(12));
        engine.End(originalId);
        engine.Archive(originalId);
        var original = Assert.Single(engine.Entries);
        var restoredId = engine.Unarchive(originalId);
        var restored = engine.Entries.Single(entry => entry.Id == restoredId);
        Assert.NotEqual(originalId, restoredId);
        Assert.Equal(original, engine.Entries.Single(entry => entry.Id == originalId));
        Assert.Equal(engine.Today, restored.Date);
        Assert.Equal(original.Label, restored.Label);
        Assert.Equal(original.Color, restored.Color);
        Assert.Equal(original.Description, restored.Description);
        Assert.Equal(TimeSpan.Zero, restored.Elapsed);
        Assert.False(restored.IsEnded);
        Assert.False(restored.IsArchived);
        Assert.False(restored.NeedsReview);
        Assert.False(engine.IsRunning(restoredId));
        clock.Advance(TimeSpan.FromHours(2));
        var recovered = new TrackerEngine(engine.Snapshot(), clock, TimeZoneInfo.Utc);
        Assert.Equal(TimeSpan.Zero, recovered.Entries.Single(entry => entry.Id == restoredId).Elapsed);
        Assert.Equal(original, recovered.Entries.Single(entry => entry.Id == originalId));
    }

    [Fact]
    public void EarlierDayCanBeRestoredWithoutClearingItsReviewWarningOrTime()
    {
        var previous = new TrackerEntry(Guid.NewGuid(), "FEATURE-8", TrackerColors.All[0],
            new DateOnly(2026, 10, 22), TimeSpan.FromMinutes(18), NeedsReview: true);
        var engine = new TrackerEngine(new(1, [previous]), clock, TimeZoneInfo.Utc);
        var restoredId = engine.Unarchive(previous.Id);
        Assert.Equal(previous, engine.Entries.Single(entry => entry.Id == previous.Id));
        var restored = engine.Entries.Single(entry => entry.Id == restoredId);
        Assert.Equal(engine.Today, restored.Date);
        Assert.Equal(TimeSpan.Zero, restored.Elapsed);
        Assert.False(restored.NeedsReview);
        Assert.False(engine.IsRunning(restoredId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedUnarchiveReusesCurrentEntryWithoutResettingOrPausingIt(bool running)
    {
        var previous = new TrackerEntry(Guid.NewGuid(), "CARD-2", TrackerColors.All[0],
            new DateOnly(2026, 10, 22), TimeSpan.FromMinutes(18), IsEnded: true, IsArchived: true);
        var current = previous with { Id = Guid.NewGuid(), Date = new DateOnly(2026, 10, 23),
            Elapsed = TimeSpan.FromMinutes(4), IsEnded = false, IsArchived = false, Description = "Current work" };
        var engine = new TrackerEngine(new(1, [previous, current]), clock, TimeZoneInfo.Utc);
        if (running) engine.Start(current.Id);
        Assert.Equal(current.Id, engine.Unarchive(previous.Id));
        Assert.Equal(current.Id, engine.Unarchive(previous.Id));
        Assert.Equal(2, engine.Entries.Count);
        Assert.Equal(current, engine.Entries.Single(entry => entry.Id == current.Id));
        Assert.Equal(running, engine.IsRunning(current.Id));
        Assert.Equal(previous, engine.Entries.Single(entry => entry.Id == previous.Id));
    }

    [Fact]
    public void EndedEntryForTodayIsPreservedWhenAnotherSessionIsPrepared()
    {
        var previous = new TrackerEntry(Guid.NewGuid(), "CARD-4", TrackerColors.All[0],
            new DateOnly(2026, 10, 22), TimeSpan.FromMinutes(18), IsEnded: true, IsArchived: true);
        var endedToday = previous with { Id = Guid.NewGuid(), Date = new DateOnly(2026, 10, 23), IsArchived = false };
        var engine = new TrackerEngine(new(1, [previous, endedToday]), clock, TimeZoneInfo.Utc);
        var restoredId = engine.Unarchive(previous.Id);
        Assert.Equal(3, engine.Entries.Count);
        Assert.Equal(endedToday, engine.Entries.Single(entry => entry.Id == endedToday.Id));
        Assert.Equal(previous, engine.Entries.Single(entry => entry.Id == previous.Id));
        var restored = engine.Entries.Single(entry => entry.Id == restoredId);
        Assert.Equal(TimeSpan.Zero, restored.Elapsed);
        Assert.False(restored.IsEnded);
        Assert.False(engine.IsRunning(restoredId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentEntriesCannotBeUnarchivedOrReset(bool ended)
    {
        var engine = new TrackerEngine(TrackerDocument.Empty, clock, TimeZoneInfo.Utc);
        var id = engine.Add("CARD-1", TrackerColors.All[0]);
        if (ended) engine.End(id);
        var original = Assert.Single(engine.Entries);
        Assert.Throws<InvalidOperationException>(() => engine.Unarchive(id));
        Assert.Equal(original, Assert.Single(engine.Entries));
    }
}
