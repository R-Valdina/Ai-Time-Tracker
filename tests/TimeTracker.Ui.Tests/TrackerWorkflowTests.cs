using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using TimeTracker.Core;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TimeTracker.Ui.Tests;

/// <summary>Exercises the shipped WPF controls through Windows UI Automation, using isolated real data files.</summary>
public sealed class TrackerWorkflowTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "TimeTracker.Ui.Tests", Guid.NewGuid().ToString("N"));
    private readonly UIA3Automation automation = new();
    private Application? application;
    private Window? window;

    [Fact]
    public void CreateStartStopEndAndReopenPreservesTotal()
    {
        Launch();
        CreateTracker("BUG-903");
        Assert.Equal("Today’s trackers", Find("TodayTrackers").Name);
        ToggleButtons()[0].Invoke();
        WaitUntil(() => ToggleButtons()[0].Name.Contains("Stop"), "Timer did not enter its running state.");
        WaitUntil(() => !Find("ElapsedTime").AsLabel().Text.Equals("00:00:00"), "Timer never accumulated time.");
        ToggleButtons()[0].Invoke();
        var total = Find("ElapsedTime").AsLabel().Text;
        OpenRowMenu("EndTracker");
        WaitUntil(() => !ToggleButtons()[0].IsEnabled, "Ended entry remained startable.");
        Assert.False(ToggleButtons()[0].IsEnabled);
        var finalTotal = Find("FinalTotal").AsLabel().Text;
        Assert.StartsWith("Total: ", finalTotal);
        Assert.False(Find("FinalTotal").IsOffscreen);
        Restart();
        Assert.Equal(total, Find("ElapsedTime").AsLabel().Text);
        Assert.False(ToggleButtons()[0].IsEnabled);
        Assert.Equal(finalTotal, Find("FinalTotal").AsLabel().Text);
        WaitForTrackerReference("BUG-903");
    }

    [Fact]
    public void EditDeleteConfirmationAndThemePreferenceWork()
    {
        Launch();
        CreateTracker("FEAT-1428", "Review acceptance criteria");
        OpenRowMenu("EditTracker");
        Assert.Equal("Review acceptance criteria", Find("TrackerDescription").AsTextBox().Text);
        Find("TrackerDescription").AsTextBox().Text = "Verify the corrected workflow";
        Find("TrackerLabel").AsTextBox().Text = "FEAT-1429";
        Find("TrackerColor").AsListBox().Items[1].Select();
        Find("SaveTracker").AsButton().Invoke();
        WaitForTrackerReference("FEAT-1429");
        Assert.Equal(TrackerColors.All[1], ReadDocument().Entries[0].Color);
        Find("Settings").AsButton().Invoke();
        Find("DarkTheme").AsMenuItem().Invoke();
        var preferencesPath = Path.Combine(dataDirectory, "preferences.json");
        // Invoke queues work in WPF. The first preferences file may not exist until that work runs.
        WaitUntil(() => File.Exists(preferencesPath) &&
            JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(preferencesPath))?.Theme == "Dark", "Theme was not saved.");
        Restart();
        Assert.Equal("Description for FEAT-1429: Verify the corrected workflow", Find("TrackerDescriptionText").Name);
        Assert.Equal("Verify the corrected workflow", ReadDocument().Entries[0].Description);
        var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Path.Combine(dataDirectory, "preferences.json")));
        Assert.Equal("Dark", preferences!.Theme);
        OpenRowMenu("DeleteTracker");
        ConfirmDeletion(false);
        Assert.Single(ToggleButtons());
        OpenRowMenu("DeleteTracker");
        ConfirmDeletion(true);
        WaitUntil(() => ToggleButtons().Length == 0, "Confirmed deletion did not remove the row.");
        Restart();
        Assert.Empty(ToggleButtons());
    }

    [Fact]
    public void ClosingPausesEveryTimerAndRestoresWindowSize()
    {
        Launch();
        CreateTracker("A");
        CreateTracker("B");
        foreach (var button in ToggleButtons()) button.Invoke();
        WaitUntil(() => ToggleButtons().All(button => button.Name.Contains("Stop")), "Independent timers did not run together.");
        window!.Patterns.Transform.Pattern.Resize(700, 600);
        Close();
        var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Path.Combine(dataDirectory, "preferences.json")));
        Launch();
        Assert.All(ToggleButtons(), button => Assert.Contains("Start", button.Name));
        // Native window dimensions include non-client chrome; saved client-independent bounds are authoritative.
        Assert.NotNull(preferences);
        Assert.InRange(preferences.Width, 650, 750);
        Assert.InRange(window!.BoundingRectangle.Width, 650, 750);
    }

    [Fact]
    public void ForcedTerminationRecoversCheckpointWithTimerStopped()
    {
        Launch();
        CreateTracker("CRASH-1");
        ToggleButtons()[0].Invoke();
        WaitUntil(() => ReadDocument().Entries[0].Elapsed > TimeSpan.Zero, "No running checkpoint was saved.");
        application!.Kill();
        WaitUntil(() => application.HasExited, "Terminated app did not exit.");
        application.Dispose();
        application = null;
        Launch();
        Assert.Contains("Start", ToggleButtons()[0].Name);
        Assert.NotEqual("00:00:00", Find("ElapsedTime").AsLabel().Text);
    }

    [Fact]
    public void MidnightWarningAndHistoricalStartAreVisible()
    {
        Directory.CreateDirectory(dataDirectory);
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var document = new TrackerDocument(1,
            [new TrackerEntry(Guid.NewGuid(), "LATE-1", TrackerColors.All[1], yesterday, TimeSpan.FromMinutes(12), NeedsReview: true)]);
        new JsonFileStore<TrackerDocument>(Path.Combine(dataDirectory, "tracker-data.json")).Save(document);
        Launch();
        WaitUntil(() => ToggleButtons().Length == 1, "History containing review warnings did not expand.");
        Assert.Equal("Earlier days trackers", Find("EarlierTrackers").Name);
        Assert.Contains("Start today", ToggleButtons()[0].Name);
        var warning = window!.FindFirstDescendant(condition => condition.ByAutomationId("MidnightWarning")
            .And(condition.ByName("⚠ Paused at midnight — review time")));
        Assert.NotNull(warning);
        Assert.False(warning!.IsOffscreen);
        ToggleButtons()[0].Invoke();
        WaitUntil(() => ToggleButtons().Length == 2, "Starting a historical card did not create today's entry.");
        Assert.Equal(2, ReadDocument().Entries.Count);
    }

    /// <summary>Archives an ended row through the menu and checks the real saved history after reopening.</summary>
    [Fact]
    public void ArchiveRetainsDateAndTotalInEarlierDays()
    {
        Launch();
        CreateTracker("ARCHIVE-1", "Investigate intermittent login failure");
        var original = ReadDocument().Entries[0];
        OpenRowMenu("EndTracker");
        WaitUntil(() => !ToggleButtons()[0].IsEnabled, "Ending did not finish the entry.");
        OpenRowMenu("ArchiveTracker");
        WaitUntil(() => ReadDocument().Entries[0].IsArchived, "Archive was not saved.");
        Assert.Empty(Find("TodayTrackers").FindAllDescendants(condition => condition.ByAutomationId("TrackerReference")));
        Assert.Equal("Earlier days trackers", Find("EarlierTrackers").Name);
        Assert.Single(Find("EarlierTrackers").FindAllDescendants(condition => condition.ByAutomationId("TrackerReference")));
        var archived = ReadDocument().Entries[0];
        Assert.Equal(original.Date, archived.Date);
        Assert.Equal(original.Elapsed, archived.Elapsed);
        Assert.True(archived.IsEnded);
        Assert.Equal("Investigate intermittent login failure", archived.Description);
        Assert.Equal($"Description for {archived.Label}: {archived.Description}", Find("TrackerDescriptionText").Name);
        Restart();
        Assert.True(ReadDocument().Entries[0].IsArchived);
        Assert.Empty(Find("TodayTrackers").FindAllDescendants(condition => condition.ByAutomationId("TrackerReference")));
    }

    /// <summary>Restores both same-day archives and older retained entries, preserving history and any existing active session.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void UnarchiveRestoresPausedTodayWithoutLosingHistory(int dayOffset, bool archived)
    {
        Directory.CreateDirectory(dataDirectory);
        var original = new TrackerEntry(Guid.NewGuid(), "RESTORE-3", TrackerColors.All[1],
            DateOnly.FromDateTime(DateTime.Now).AddDays(dayOffset), TimeSpan.FromMinutes(12),
            IsEnded: archived, NeedsReview: !archived, IsArchived: archived, Description: "Verify the restored card");
        new JsonFileStore<TrackerDocument>(Path.Combine(dataDirectory, "tracker-data.json")).Save(new(1, [original]));
        Launch();
        Find("HistorySection").Patterns.ExpandCollapse.Pattern.Expand();
        OpenRowMenu("UnarchiveTracker", "EarlierTrackers");
        WaitUntil(() => ReadDocument().Entries.Count == 2, "Unarchive did not create today's paused entry.");
        WaitForTrackerReference("RESTORE-3");
        var current = ReadDocument().Entries.Single(entry => entry.Id != original.Id);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), current.Date);
        Assert.Equal(TimeSpan.Zero, current.Elapsed);
        Assert.False(current.IsEnded);
        Assert.False(current.IsArchived);
        Assert.Equal(original.Description, current.Description);
        Assert.Equal(original.Color, current.Color);
        Assert.Equal(original, ReadDocument().Entries.Single(entry => entry.Id == original.Id));
        var today = Find("TodayTrackers");
        WaitUntil(() => today.FindFirstDescendant(condition => condition.ByAutomationId("ToggleTimer")) is not null,
            "The restored card was not rendered in today's list.");
        var toggle = today.FindFirstDescendant(condition => condition.ByAutomationId("ToggleTimer"));
        Assert.NotNull(toggle);
        Assert.Equal("Start timer for RESTORE-3", toggle.Name);
        Restart();
        Assert.Equal(original, ReadDocument().Entries.Single(entry => entry.Id == original.Id));
        Assert.Equal(TimeSpan.Zero, ReadDocument().Entries.Single(entry => entry.Id == current.Id).Elapsed);
        Find("TodayTrackers").FindFirstDescendant(condition => condition.ByAutomationId("ToggleTimer"))!.AsButton().Invoke();
        WaitUntil(() => Find("TodayTrackers").FindFirstDescendant(condition => condition.ByAutomationId("ToggleTimer"))!.Name == "Stop timer for RESTORE-3",
            "The restored card could not be started.");
        Find("HistorySection").Patterns.ExpandCollapse.Pattern.Expand();
        OpenRowMenu("UnarchiveTracker", "EarlierTrackers");
        Assert.Equal(2, ReadDocument().Entries.Count);
        Assert.Equal("Stop timer for RESTORE-3", Find("TodayTrackers").FindFirstDescendant(condition => condition.ByAutomationId("ToggleTimer"))!.Name);
    }

    /// <summary>Checks the generated data-item peers Narrator reads, rather than only the enclosing list labels.</summary>
    [Fact]
    public void TrackerItemsExposeCardNamesInsteadOfInternalTypeNames()
    {
        Launch();
        CreateTracker("NARRATOR-1");
        CreateTracker("NARRATOR-2");
        Assert.Equal("Trackers", Find("TrackerArea").Name);
        var today = Find("TodayTrackers");
        var items = today.FindAllDescendants(condition => condition.ByControlType(ControlType.DataItem));
        Assert.Equal(2, items.Length);
        Assert.Equal(new[] { "Tracker NARRATOR-1", "Tracker NARRATOR-2" }, items.Select(item => item.Name).ToArray());

        OpenRowMenu("EditTracker");
        Find("TrackerLabel").AsTextBox().Text = "NARRATOR-RENAMED";
        Find("SaveTracker").AsButton().Invoke();
        WaitForTrackerReference("NARRATOR-RENAMED");
        WaitUntil(() => today.FindFirstDescendant(condition => condition.ByControlType(ControlType.DataItem)
            .And(condition.ByName("Tracker NARRATOR-RENAMED"))) is not null, "The accessible row name did not follow the edited card reference.");

        OpenRowMenu("EndTracker");
        WaitUntil(() => !ToggleButtons()[0].IsEnabled, "Ending did not finish the entry.");
        OpenRowMenu("ArchiveTracker");
        var history = Find("EarlierTrackers");
        WaitUntil(() => history.FindAllDescendants(condition => condition.ByControlType(ControlType.DataItem)).Length == 1,
            "The archived accessibility item was not exposed.");
        Assert.Equal("Tracker NARRATOR-RENAMED", Assert.Single(history.FindAllDescendants(condition => condition.ByControlType(ControlType.DataItem))).Name);
        Assert.Equal("Tracker NARRATOR-2", Assert.Single(today.FindAllDescendants(condition => condition.ByControlType(ControlType.DataItem))).Name);
    }

    /// <summary>Uses real Tab and Shift+Tab navigation to reach populated notes in current and archived rows.</summary>
    [Fact]
    public void DescriptionsParticipateInKeyboardNavigationOnlyWhenPopulated()
    {
        Launch();
        const string description = "Verify acceptance criteria and investigate the intermittent login failure.";
        CreateTracker("NOTE-1", description);
        CreateTracker("EMPTY-2");
        var notes = Find("TodayTrackers").FindAllDescendants(condition => condition.ByAutomationId("TrackerDescriptionText"));
        Assert.Equal(2, notes.Length);
        Assert.Equal($"Description for NOTE-1: {description}", notes[0].Name);
        Assert.True(notes[0].Properties.IsKeyboardFocusable.Value);
        Assert.False(notes[1].Properties.IsKeyboardFocusable.Value);

        ToggleButtons()[0].Focus();
        Keyboard.Type(VirtualKeyShort.TAB);
        WaitUntil(() => notes[0].Properties.HasKeyboardFocus.Value, "Tab skipped the populated tracker description.");
        Keyboard.Type(VirtualKeyShort.TAB);
        var actions = window!.FindAllDescendants(condition => condition.ByAutomationId("TrackerActions"));
        WaitUntil(() => actions[1].Properties.HasKeyboardFocus.Value, "Tab did not leave the description for the next tracker.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
        WaitUntil(() => notes[0].Properties.HasKeyboardFocus.Value, "Shift+Tab skipped the populated description.");
        ToggleButtons()[1].Focus();
        Keyboard.Type(VirtualKeyShort.TAB);
        Assert.False(notes[1].Properties.HasKeyboardFocus.Value);

        OpenRowMenu("EndTracker");
        WaitUntil(() => !ToggleButtons()[0].IsEnabled, "Ending did not finish the entry.");
        OpenRowMenu("ArchiveTracker");
        var history = Find("EarlierTrackers");
        var archivedNote = history.FindFirstDescendant(condition => condition.ByAutomationId("TrackerDescriptionText"));
        Assert.NotNull(archivedNote);
        var archivedActions = history.FindFirstDescendant(condition => condition.ByAutomationId("TrackerActions"));
        Assert.NotNull(archivedActions);
        archivedActions.Focus();
        Keyboard.Type(VirtualKeyShort.TAB);
        WaitUntil(() => archivedNote!.Properties.HasKeyboardFocus.Value, "Tab skipped the archived description.");
        Assert.Equal($"Description for NOTE-1: {description}", archivedNote!.Name);
    }

    /// <summary>Sorts through the visible card-order controls while keeping the running card and saved insertion order intact.</summary>
    [Fact]
    public void SortOrderUsesCardNumbersPersistsAndFollowsEdits()
    {
        Launch();
        CreateTracker("CARD-10");
        CreateTracker("CARD-1");
        CreateTracker("CARD-2");
        ToggleButtons()[0].Invoke();
        WaitUntil(() => ToggleButtons()[0].Name == "Stop timer for CARD-10", "The test timer did not start.");
        SelectSort("SortAscending");
        WaitUntil(() => RowReferences("TodayTrackers").SequenceEqual(new[] { "CARD-1", "CARD-2", "CARD-10" }), "Ascending card order was not applied.");
        Assert.Equal("Stop timer for CARD-10", ToggleButtons()[2].Name);
        SelectSort("SortDescending");
        WaitUntil(() => RowReferences("TodayTrackers").SequenceEqual(new[] { "CARD-10", "CARD-2", "CARD-1" }), "Descending card order was not applied.");
        Assert.Equal("Stop timer for CARD-10", ToggleButtons()[0].Name);
        Assert.Equal(new[] { "CARD-10", "CARD-1", "CARD-2" }, ReadDocument().Entries.Select(entry => entry.Label));
        Restart();
        Assert.Equal(new[] { "CARD-10", "CARD-2", "CARD-1" }, RowReferences("TodayTrackers"));
        SelectSort("SortAdded");
        WaitUntil(() => RowReferences("TodayTrackers").SequenceEqual(new[] { "CARD-10", "CARD-1", "CARD-2" }), "Original added order was not restored.");
        SelectSort("SortAscending");
        WaitUntil(() => RowReferences("TodayTrackers")[0] == "CARD-1", "Ascending order was not restored.");
        OpenRowMenu("EditTracker");
        Find("TrackerLabel").AsTextBox().Text = "CARD-20";
        Find("SaveTracker").AsButton().Invoke();
        WaitUntil(() => RowReferences("TodayTrackers").SequenceEqual(new[] { "CARD-2", "CARD-10", "CARD-20" }), "Editing the reference did not reposition its row.");
    }

    /// <summary>Selects the visible radio control through its native accessibility selection pattern.</summary>
    private void SelectSort(string automationId)
    {
        Find(automationId).Patterns.SelectionItem.Pattern.Select();
    }

    /// <summary>Reads reference controls in the same order a user encounters the displayed rows.</summary>
    private string[] RowReferences(string listId) => Find(listId)
        .FindAllDescendants(condition => condition.ByAutomationId("TrackerReference")).Select(element => element.Name).ToArray();

    /// <summary>Checks real mouse focus cleanup, retained keyboard focus, and names exposed to screen readers.</summary>
    [Fact]
    public void MouseActionsReleaseFocusWhileKeyboardActionsKeepAccessibleTargeting()
    {
        Launch();
        var create = Find("NewTracker").AsButton();
        create.Click();
        var label = Find("TrackerLabel").AsTextBox();
        Assert.Equal("Card reference", label.Name);
        WaitUntil(() => label.Properties.HasKeyboardFocus.Value, "The editor's input lost its initial focus.");
        label.Text = "FOCUS-1";
        Find("SaveTracker").AsButton().Click();
        WaitForTrackerReference("FOCUS-1");
        WaitUntil(() => !create.Properties.HasKeyboardFocus.Value, "The completed New tracker action retained focus.");

        var toggle = ToggleButtons()[0];
        Assert.Equal("Start timer for FOCUS-1", toggle.Name);
        Assert.Equal("Actions for FOCUS-1", Find("TrackerActions").Name);
        toggle.Click();
        WaitUntil(() => toggle.Name == "Stop timer for FOCUS-1", "Mouse click did not start the card.");
        WaitUntil(() => !toggle.Properties.HasKeyboardFocus.Value, "The mouse-clicked timer retained focus.");

        toggle.Focus();
        Keyboard.Type(VirtualKeyShort.SPACE);
        WaitUntil(() => toggle.Name == "Start timer for FOCUS-1", "Keyboard activation did not stop the card.");
        Assert.True(toggle.Properties.HasKeyboardFocus.Value);

        var settings = Find("Settings").AsButton();
        settings.Click();
        Find("DarkTheme").AsMenuItem().Click();
        var preferencesPath = Path.Combine(dataDirectory, "preferences.json");
        WaitUntil(() => File.Exists(preferencesPath) && JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(preferencesPath))?.Theme == "Dark", "The mouse-selected theme was not saved.");
        WaitUntil(() => !settings.Properties.HasKeyboardFocus.Value, "Closing the menu restored focus to its mouse-clicked button.");
        var ascending = Find("SortAscending").AsRadioButton();
        ascending.Click();
        WaitUntil(() => JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(preferencesPath))?.SortOrder == TrackerSortOrder.Ascending,
            "Mouse selection did not save the ascending sort choice.");
        WaitUntil(() => !ascending.Properties.HasKeyboardFocus.Value, "The mouse-selected sort choice retained focus.");
        var descending = Find("SortDescending").AsRadioButton();
        descending.Focus();
        Keyboard.Type(VirtualKeyShort.SPACE);
        WaitUntil(() => JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(preferencesPath))?.SortOrder == TrackerSortOrder.Descending,
            "Keyboard selection did not save the descending sort choice.");
        Assert.True(descending.Properties.HasKeyboardFocus.Value);
    }

    /// <summary>Launches a real executable; missing prerequisites fail instead of producing a zero-test success.</summary>
    private void Launch()
    {
        var executable = Environment.GetEnvironmentVariable("TIME_TRACKER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable))
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "TimeTracker.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            executable = Path.Combine(root.FullName, "src", "TimeTracker.Desktop", "bin", configuration, "net10.0-windows", "TimeTracker.exe");
        }
        Assert.True(File.Exists(executable), $"Build the desktop app in Release first, or set TIME_TRACKER_EXECUTABLE. Missing: {executable}");
        var startInfo = new ProcessStartInfo(executable);
        startInfo.ArgumentList.Add("--data-directory");
        startInfo.ArgumentList.Add(dataDirectory);
        application = Application.Launch(startInfo);
        window = application.GetMainWindow(automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);
        Assert.Equal("Time Tracker", window.Title);
    }

    /// <summary>Creates a tracker through the same editor a user operates.</summary>
    private void CreateTracker(string label, string description = "")
    {
        Find("NewTracker").AsButton().Invoke();
        Find("TrackerLabel").AsTextBox().Text = label;
        Find("TrackerDescription").AsTextBox().Text = description;
        Find("SaveTracker").AsButton().Invoke();
        WaitForTrackerReference(label);
    }

    /// <summary>Matches the actual reference control without reading unsupported Name properties on unrelated elements.</summary>
    private void WaitForTrackerReference(string label) =>
        WaitUntil(() => window!.FindFirstDescendant(condition => condition.ByAutomationId("TrackerReference")
            .And(condition.ByName(label))) is not null, $"Card reference '{label}' was not displayed.");

    /// <summary>Returns visible toggle controls from the actual main window.</summary>
    private Button[] ToggleButtons() => window!.FindAllDescendants(condition => condition.ByAutomationId("ToggleTimer")).Select(element => element.AsButton()).ToArray();

    /// <summary>Finds dialogs and popup elements across the desktop, tolerating asynchronous rendering.</summary>
    private AutomationElement Find(string automationId)
    {
        var result = Retry.WhileNull(() => automation.GetDesktop().FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
            timeout: TimeSpan.FromSeconds(10), interval: TimeSpan.FromMilliseconds(100));
        Assert.NotNull(result.Result);
        return result.Result;
    }

    /// <summary>Invokes an action in the first row's context menu through accessibility patterns.</summary>
    private void OpenRowMenu(string actionId, string? listId = null)
    {
        var actions = listId is null ? Find("TrackerActions") :
            Find(listId).FindFirstDescendant(condition => condition.ByAutomationId("TrackerActions"));
        Assert.NotNull(actions);
        actions.AsButton().Invoke();
        var action = Find(actionId).AsMenuItem();
        if (actionId == "UnarchiveTracker") Assert.Equal("Unarchive", action.Name);
        action.Invoke();
    }

    /// <summary>Responds to the themed confirmation dialog using stable, language-independent identifiers.</summary>
    private void ConfirmDeletion(bool delete)
    {
        var dialog = Retry.WhileNull(() => window!.ModalWindows.FirstOrDefault(candidate => candidate.Title == "Delete tracker"), timeout: TimeSpan.FromSeconds(10)).Result;
        Assert.NotNull(dialog);
        var button = dialog!.FindFirstDescendant(condition => condition.ByAutomationId(delete ? "ConfirmAction" : "CancelAction"));
        Assert.NotNull(button);
        button!.AsButton().Invoke();
        WaitUntil(() => window!.ModalWindows.Length == 0, "Deletion confirmation did not close.");
    }

    /// <summary>Waits for observable application state instead of relying on arbitrary sleeps.</summary>
    private static void WaitUntil(Func<bool> condition, string failure)
    {
        var result = Retry.WhileFalse(condition, timeout: TimeSpan.FromSeconds(15), interval: TimeSpan.FromMilliseconds(100));
        Assert.True(result.Result, failure);
    }

    /// <summary>Reads a completed atomic checkpoint from the test-specific data directory.</summary>
    private TrackerDocument ReadDocument() =>
        new JsonFileStore<TrackerDocument>(Path.Combine(dataDirectory, "tracker-data.json"), TrackerEngine.Validate).Load(() => TrackerDocument.Empty).Value;

    /// <summary>Stops through the normal close path and waits for the process to release its data lock.</summary>
    private void Close()
    {
        if (application is null) return;
        window!.Close();
        WaitUntil(() => application.HasExited, "Application did not exit after closing.");
        application.Dispose();
        application = null;
    }

    /// <summary>Checks persistence using a fresh application process.</summary>
    private void Restart() { Close(); Launch(); }

    /// <summary>Always terminates only this fixture's process and removes its isolated files.</summary>
    public void Dispose()
    {
        if (application is not null)
        {
            if (!application.HasExited) application.Kill();
            application.Dispose();
        }
        automation.Dispose();
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
    }
}
