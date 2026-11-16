using System.Text.Json;

namespace TimeTracker.Core.Tests;

/// <summary>Coverage for batch creation and optional work types.</summary>
public sealed class BulkTrackerTests
{
    [Fact]
    public void CreatesAllProvidedReferencesWithSharedMetadata()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var ids = engine.AddMany("25-02472, 25-03342, 25-05522, 26-10402, 26-10354, 26-10860, 26-10861, 26-10862, 26-10863, 26-09224, 26-08972", TrackerColors.All[0], " note ", " Testing ");
        Assert.Equal(11, ids.Count);
        Assert.Equal(11, ids.Distinct().Count());
        Assert.All(engine.Entries, entry =>
        {
            Assert.Equal("Testing", entry.Type);
            Assert.Equal("note", entry.Description);
            Assert.Equal(TimeSpan.Zero, entry.Elapsed);
            Assert.False(engine.IsRunning(entry.Id));
        });
        Assert.Equal("26-08972", engine.Entries.Last().Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("first,,last")]
    [InlineData("first, ")]
    public void InvalidBatchDoesNotCreateAnyEntries(string references)
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        Assert.Throws<ArgumentException>(() => engine.AddMany(references, TrackerColors.All[0]));
        Assert.Empty(engine.Entries);
    }

    [Fact]
    public void InvalidMetadataOrReferenceDoesNotPartiallyCreateBatch()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        Assert.Throws<ArgumentException>(() => engine.AddMany("first," + new string('x', 121), TrackerColors.All[0]));
        Assert.Throws<ArgumentException>(() => engine.AddMany("first,last", TrackerColors.All[0], type: new string('x', 21)));
        Assert.Throws<ArgumentException>(() => engine.Add("first", TrackerColors.All[0], type: "Test\nPlan"));
        Assert.Empty(engine.Entries);
    }

    [Fact]
    public void TypeCanBeEditedAndRoundTripsWithExistingDataDefaultingToBlank()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var id = engine.Add("first", TrackerColors.All[0]);
        Assert.Equal("", Assert.Single(engine.Entries).Type);
        engine.Edit(id, "first", TrackerColors.All[0], type: new string('x', 20));
        var json = JsonSerializer.Serialize(engine.Snapshot());
        var restored = new TrackerEngine(JsonSerializer.Deserialize<TrackerDocument>(json)!);
        Assert.Equal(new string('x', 20), Assert.Single(restored.Entries).Type);
        var legacyJson = json.Replace(",\"Type\":\"" + new string('x', 20) + "\"", "");
        Assert.Equal("", Assert.Single(new TrackerEngine(JsonSerializer.Deserialize<TrackerDocument>(legacyJson)!).Entries).Type);
        Assert.Throws<ArgumentException>(() => engine.Edit(id, "first", TrackerColors.All[0], type: new string('x', 21)));
    }

    [Fact]
    public void RestorationPreservesTypeAndDoesNotReuseAnotherWorkType()
    {
        var engine = new TrackerEngine(TrackerDocument.Empty);
        var source = engine.Add("first", TrackerColors.All[0], type: "Testing");
        engine.EndAndArchive(source);
        engine.Add("first", TrackerColors.All[0], type: "Publishing");
        var restored = engine.Unarchive(source);
        Assert.NotEqual(source, restored);
        Assert.Equal("Testing", engine.Entries.Single(entry => entry.Id == restored).Type);
        Assert.Equal(restored, engine.Unarchive(source));
    }
}
