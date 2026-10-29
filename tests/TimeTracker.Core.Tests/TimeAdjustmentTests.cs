namespace TimeTracker.Core.Tests;

/// <summary>Checks correction parsing, running checkpoints, reset rules, and recovery without wall-clock delays.</summary>
public sealed class TimeAdjustmentTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData(" 0:05 ", 5)]
    [InlineData("25:00", 1500)]
    [InlineData("1", 1)]
    public void ParsesWholeMinutesAndHours(string input, int minutes)
    {
        Assert.True(TimeAdjustment.TryParse(input, out var duration));
        Assert.Equal(TimeSpan.FromMinutes(minutes), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("1:60")]
    [InlineData("1:-2")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("1:20:30")]
    [InlineData("abc")]
    [InlineData("9999999999999999999")]
    [InlineData("999999999999:00")]
    public void RejectsInvalidOrUnrepresentableInput(string? input) => Assert.False(TimeAdjustment.TryParse(input, out _));

    [Fact]
    public void CorrectionsCheckpointRunningTimeWithoutChangingOtherTimers()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        var engine = new TrackerEngine(TrackerDocument.Empty, clock, TimeZoneInfo.Utc);
        var first = engine.Add("FIRST", TrackerColors.All[0]);
        var second = engine.Add("SECOND", TrackerColors.All[1]);
        engine.Start(first);
        engine.Start(second);
        clock.Advance(TimeSpan.FromSeconds(30));
        engine.AdjustTime(first, TimeSpan.FromMinutes(10), false);
        clock.Advance(TimeSpan.FromSeconds(30));
        engine.AdjustTime(first, TimeSpan.FromMinutes(3), true);
        Assert.Equal(TimeSpan.FromMinutes(8), engine.Entries.Single(entry => entry.Id == first).Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(1), engine.Entries.Single(entry => entry.Id == second).Elapsed);
        Assert.True(engine.IsRunning(first));
        engine.ResetTime(first);
        Assert.False(engine.IsRunning(first));
        Assert.True(engine.IsRunning(second));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.Zero, engine.Snapshot().Entries.Single(entry => entry.Id == first).Elapsed);
    }

    [Fact]
    public void SubtractionStopsAtZeroAndInvalidAmountsLeaveTotalIntact()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var id = engine.Add("CARD", TrackerColors.All[0]);
        engine.AdjustTime(id, TimeSpan.FromMinutes(3), false);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.AdjustTime(id, TimeSpan.Zero, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.AdjustTime(id, TimeSpan.FromMinutes(-1), true));
        Assert.Equal(TimeSpan.FromMinutes(3), engine.Entries[0].Elapsed);
        engine.AdjustTime(id, TimeSpan.FromMinutes(5), true);
        Assert.Equal(TimeSpan.Zero, engine.Entries[0].Elapsed);
    }

    [Fact]
    public void EndedTotalsCanBeCorrectedAndResetReopensTodaysCard()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var id = engine.Add("CARD", TrackerColors.All[0], "Keep this note");
        engine.End(id);
        engine.AdjustTime(id, TimeSpan.FromMinutes(90), false);
        var restored = new TrackerEngine(engine.Snapshot());
        Assert.True(restored.Entries[0].IsEnded);
        Assert.Equal(TimeSpan.FromMinutes(90), restored.Entries[0].Elapsed);
        restored.ResetTime(id);
        Assert.False(restored.Entries[0].IsEnded);
        Assert.Equal("Keep this note", restored.Entries[0].Description);
        Assert.Equal(TimeSpan.Zero, restored.Entries[0].Elapsed);
        restored.Start(id);
        Assert.True(restored.IsRunning(id));
    }

    [Fact]
    public void HistoryCorrectionsAndResetRetainDateArchiveAndReviewFlags()
    {
        var entry = new TrackerEntry(Guid.NewGuid(), "OLD", TrackerColors.All[0], DateOnly.FromDateTime(DateTime.Now).AddDays(-1),
            TimeSpan.FromHours(2), IsEnded: true, NeedsReview: true, IsArchived: true);
        var engine = new TrackerEngine(new TrackerDocument(1, [entry]));
        engine.AdjustTime(entry.Id, TimeSpan.FromMinutes(30), true);
        Assert.Equal(entry with { Elapsed = TimeSpan.FromMinutes(90) }, engine.Entries[0]);
        engine.ResetTime(entry.Id);
        Assert.Equal(entry with { Elapsed = TimeSpan.Zero }, engine.Entries[0]);
    }

    [Fact]
    public void OverflowDoesNotReplaceRecordedTotal()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var id = engine.Add("CARD", TrackerColors.All[0]);
        engine.AdjustTime(id, TimeSpan.FromMinutes(5), false);
        Assert.Throws<OverflowException>(() => engine.AdjustTime(id, TimeSpan.MaxValue, false));
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Entries[0].Elapsed);
    }
}
