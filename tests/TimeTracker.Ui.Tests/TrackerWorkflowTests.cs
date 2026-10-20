using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
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
        CreateTracker("FEAT-1428");
        OpenRowMenu("EditTracker");
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
        Assert.Contains("Start today", ToggleButtons()[0].Name);
        var warning = window!.FindFirstDescendant(condition => condition.ByAutomationId("MidnightWarning")
            .And(condition.ByName("⚠ Paused at midnight — review time")));
        Assert.NotNull(warning);
        Assert.False(warning!.IsOffscreen);
        ToggleButtons()[0].Invoke();
        WaitUntil(() => ToggleButtons().Length == 2, "Starting a historical card did not create today's entry.");
        Assert.Equal(2, ReadDocument().Entries.Count);
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
    private void CreateTracker(string label)
    {
        Find("NewTracker").AsButton().Invoke();
        Find("TrackerLabel").AsTextBox().Text = label;
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
    private void OpenRowMenu(string actionId)
    {
        Find("TrackerActions").AsButton().Invoke();
        Find(actionId).AsMenuItem().Invoke();
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
