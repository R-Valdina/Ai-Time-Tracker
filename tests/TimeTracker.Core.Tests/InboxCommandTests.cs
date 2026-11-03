using System.Text.Json;

namespace TimeTracker.Core.Tests;

/// <summary>Checks posting metadata, legacy migration, command validation, and repeat-safe timer transitions.</summary>
public sealed class InboxCommandTests
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Creates the engine with deterministic timing and an arbitrary free-text label.</summary>
    private TrackerEngine CreateEngine() => new(TrackerDocument.Empty, clock, TimeZoneInfo.Utc);

    [Fact]
    public void LegacyDocumentKeepsAllEntryValuesAndSavesVersionTwoWithNullableMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Migration.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "tracker-data.json");
            const string legacy = """
                {"Version":1,"Entries":[{"Id":"4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8","Label":"any free text / card","Color":"#2563EB","Date":"2026-11-02","Elapsed":"01:30:00","IsEnded":true,"NeedsReview":true,"IsArchived":true,"Description":"Keep this note"}]}
                """;
            File.WriteAllText(path, legacy);
            var store = new JsonFileStore<TrackerDocument>(path, TrackerEngine.Validate);
            var loaded = store.Load(() => TrackerDocument.Empty).Value;
            var original = Assert.Single(loaded.Entries);
            Assert.Null(original.TimecardId);
            Assert.Null(original.PostedAt);
            Assert.False(original.IsFiled);
            var engine = new TrackerEngine(loaded, clock, TimeZoneInfo.Utc);
            Assert.Equal(original, Assert.Single(engine.Entries));
            store.Save(engine.Snapshot());
            Assert.Equal(2, store.Load(() => TrackerDocument.Empty).Value.Version);
            Assert.Equal(original, Assert.Single(store.Load(() => TrackerDocument.Empty).Value.Entries));
            Assert.Equal(legacy, File.ReadAllText(store.BackupPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void EndFreezesOnlyItsTimerAndRepeatsWithoutChangingTheTotal()
    {
        var engine = CreateEngine();
        var id = engine.Add("any card", TrackerColors.All[0]);
        var other = engine.Add("other", TrackerColors.All[0]);
        engine.Start(id);
        engine.Start(other);
        clock.Advance(TimeSpan.FromMinutes(3));
        var command = InboxCommand.Parse(JsonSerializer.Serialize(new { Command = "end", RowId = id, TimecardId = 123, PostedAt = "ignored" }));
        engine.ApplyCommand(command);
        clock.Advance(TimeSpan.FromMinutes(2));
        engine.ApplyCommand(command);
        Assert.Equal(TimeSpan.FromMinutes(3), engine.Entries.Single(entry => entry.Id == id).Elapsed);
        Assert.True(engine.Entries.Single(entry => entry.Id == id).IsEnded);
        Assert.False(engine.IsRunning(id));
        Assert.True(engine.IsRunning(other));
        Assert.False(engine.Entries[0].IsArchived);
        Assert.Null(engine.Entries[0].TimecardId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteStampsEndsArchivesAndRoundTripsInOneSnapshot(bool alreadyEnded)
    {
        var engine = CreateEngine();
        var id = engine.Add("free label", TrackerColors.All[0]);
        engine.AdjustTime(id, TimeSpan.FromMinutes(90), false);
        if (alreadyEnded) engine.End(id); else engine.Start(id);
        var posted = new DateTimeOffset(2026, 11, 3, 8, 12, 0, TimeSpan.FromHours(-5));
        var command = InboxCommand.Parse(JsonSerializer.Serialize(new { Command = "complete", RowId = id, TimecardId = "external-guid-or-text", PostedAt = posted }));
        engine.ApplyCommand(command);
        var entry = Assert.Single(engine.Entries);
        Assert.True(entry.IsEnded && entry.IsArchived && entry.IsFiled);
        Assert.False(engine.IsRunning(id));
        Assert.Equal("external-guid-or-text", entry.TimecardId);
        Assert.Equal(posted, entry.PostedAt);
        var roundTrip = JsonSerializer.Deserialize<TrackerDocument>(JsonSerializer.Serialize(engine.Snapshot()))!;
        Assert.Equal(2, roundTrip.Version);
        Assert.Equal(entry, Assert.Single(roundTrip.Entries));
        clock.Advance(TimeSpan.FromMinutes(4));
        engine.ApplyCommand(command with { PostedAt = posted.AddHours(1) });
        Assert.Equal(entry, Assert.Single(engine.Entries));
        Assert.Throws<InvalidOperationException>(() => engine.ApplyCommand(command with { TimecardId = "different" }));
        Assert.Equal(entry, Assert.Single(engine.Entries));
    }

    [Fact]
    public void UnknownRowsAndInvalidCommandsDoNotAffectRunningTrackers()
    {
        var engine = CreateEngine();
        var id = engine.Add("still running", TrackerColors.All[0]);
        engine.Start(id);
        Assert.Throws<KeyNotFoundException>(() => engine.ApplyCommand(new("end", Guid.NewGuid())));
        Assert.Throws<InvalidDataException>(() => engine.ApplyCommand(new("unknown", id)));
        Assert.Throws<InvalidDataException>(() => engine.ApplyCommand(new("complete", id)));
        Assert.True(engine.IsRunning(id));
        Assert.False(engine.Entries[0].IsEnded);
    }

    [Fact]
    public void EndAndArchiveAllowsUnfiledEntriesWithoutChangingMetadata()
    {
        var engine = CreateEngine();
        var id = engine.Add("still free text", TrackerColors.All[0], "notes");
        engine.AdjustTime(id, TimeSpan.FromMinutes(15), false);
        engine.EndAndArchive(id);
        engine.EndAndArchive(id);
        var entry = Assert.Single(engine.Entries);
        Assert.True(entry.IsEnded && entry.IsArchived);
        Assert.False(entry.IsFiled);
        Assert.Equal(TimeSpan.FromMinutes(15), entry.Elapsed);
        Assert.Equal("notes", entry.Description);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"Command\":\"unknown\",\"RowId\":\"4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8\"}")]
    [InlineData("{\"Command\":\"end\",\"RowId\":\"invalid\"}")]
    [InlineData("{\"Command\":\"end\",\"RowId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"Command\":\"complete\",\"RowId\":\"4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8\"}")]
    [InlineData("{\"Command\":\"complete\",\"RowId\":\"4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8\",\"TimecardId\":\" \",\"PostedAt\":\"2026-11-03T12:00:00Z\"}")]
    [InlineData("{\"Command\":\"complete\",\"RowId\":\"4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8\",\"TimecardId\":\"TC1\",\"PostedAt\":\"yesterday\"}")]
    public void RejectsMalformedCommands(string json)
    {
        var exception = Record.Exception(() => InboxCommand.Parse(json));
        Assert.NotNull(exception);
        Assert.True(exception is JsonException or InvalidDataException);
    }
}
