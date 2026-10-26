namespace TimeTracker.Core.Tests;

/// <summary>Exercises real temporary files, replacement, backup recovery, and malformed-data handling.</summary>
public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the same validated store used by the desktop app in an isolated directory.</summary>
    private JsonFileStore<TrackerDocument> Store() => new(Path.Combine(directory, "tracker-data.json"), TrackerEngine.Validate);

    /// <summary>Builds a valid persisted entry with an easily distinguishable total.</summary>
    private static TrackerDocument Document(int minutes) => new(1,
        [new TrackerEntry(Guid.NewGuid(), "BUG-42", TrackerColors.All[1], new DateOnly(2026, 10, 20), TimeSpan.FromMinutes(minutes))]);

    [Fact]
    public void MissingFilesCreateEmptyStateWithoutWritingAnything()
    {
        var result = Store().Load(() => TrackerDocument.Empty);
        Assert.Empty(result.Value.Entries);
        Assert.Null(result.Warning);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RepeatedSavesRoundTripAndRetainPreviousGoodVersion()
    {
        var store = Store();
        store.Save(Document(10));
        store.Save(Document(20));
        Assert.Equal(TimeSpan.FromMinutes(20), Store().Load(() => TrackerDocument.Empty).Value.Entries[0].Elapsed);
        Assert.Contains("00:10:00", File.ReadAllText(store.BackupPath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Version\":1,\"Entries\":null}")]
    [InlineData("{\"Version\":99,\"Entries\":[]}")]
    public void InvalidPrimaryRecoversBackupAndDoesNotOverwriteItWithCorruption(string invalidJson)
    {
        var store = Store();
        store.Save(Document(10));
        var goodBackup = File.ReadAllText(store.BackupPath);
        File.WriteAllText(store.FilePath, invalidJson);
        var recoveredStore = Store();
        var recovered = recoveredStore.Load(() => TrackerDocument.Empty);
        Assert.NotNull(recovered.Warning);
        Assert.Equal(TimeSpan.FromMinutes(10), recovered.Value.Entries[0].Elapsed);
        recoveredStore.Save(recovered.Value);
        Assert.Equal(goodBackup, File.ReadAllText(store.BackupPath));
        Assert.Null(Store().Load(() => TrackerDocument.Empty).Warning);
    }

    [Fact]
    public void MissingPrimaryRecoversExistingBackup()
    {
        var store = Store();
        store.Save(Document(10));
        File.Delete(store.FilePath);
        var recovered = Store().Load(() => TrackerDocument.Empty);
        Assert.NotNull(recovered.Warning);
        Assert.Equal(TimeSpan.FromMinutes(10), recovered.Value.Entries[0].Elapsed);
    }

    [Fact]
    public void TwoDamagedFilesFailLoudlyAndRemainUntouched()
    {
        var store = Store();
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.FilePath, "damaged primary");
        File.WriteAllText(store.BackupPath, "damaged backup");
        Assert.Throws<InvalidDataException>(() => store.Load(() => TrackerDocument.Empty));
        Assert.Equal("damaged primary", File.ReadAllText(store.FilePath));
        Assert.Equal("damaged backup", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void FailedSaveReportsErrorInsteadOfPretendingDataWasWritten()
    {
        Directory.CreateDirectory(directory);
        var blockingFile = Path.Combine(directory, "not-a-folder");
        File.WriteAllText(blockingFile, "keep");
        var store = new JsonFileStore<TrackerDocument>(Path.Combine(blockingFile, "tracker-data.json"));
        Assert.ThrowsAny<IOException>(() => store.Save(Document(10)));
        Assert.Equal("keep", File.ReadAllText(blockingFile));
    }

    [Fact]
    public void PreferencesRoundTripThemeAndWindowPlacement()
    {
        var store = new JsonFileStore<UserPreferences>(Path.Combine(directory, "preferences.json"));
        var preferences = new UserPreferences("Dark", 640, 800, 30, 50, true, true, TrackerSortOrder.Descending);
        store.Save(preferences);
        Assert.Equal(preferences, store.Load(() => new UserPreferences()).Value);
    }

    [Fact]
    public void OlderPreferencesWithoutSortOrderUseAscendingOrder()
    {
        var preferences = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>("{\"Theme\":\"Light\"}");
        Assert.NotNull(preferences);
        Assert.Equal(TrackerSortOrder.Ascending, preferences.SortOrder);
    }

    /// <summary>Removes only the directory created by this test instance.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
