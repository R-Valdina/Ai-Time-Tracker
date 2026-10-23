using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Connects WPF dialogs, window placement, and Windows lifecycle events to the application model.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly DispatcherTimer timer;
    private int ticksSinceSave;
    private bool automaticResize;
    private bool ready;
    private bool reviewWarningsShown;

    /// <summary>Loads local state and subscribes to Windows lock, suspend, and appearance changes.</summary>
    public MainWindow(string dataDirectory)
    {
        model = new MainViewModel(dataDirectory);
        ThemeManager.Apply(model.Preferences.Theme);
        InitializeComponent();
        ThemeManager.InitializeWindow(this);
        DataContext = model;
        RestorePlacement();
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, Tick, Dispatcher);
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += WindowsPreferenceChanged;
        Application.Current.SessionEnding += SessionEnding;
        SourceInitialized += SourceReady;
        Closing += WindowClosing;
        Closed += WindowClosed;
        PreviewKeyDown += HandleShortcut;
        Loaded += (_, _) =>
        {
            ready = true;
            AdjustHeight();
            reviewWarningsShown = model.HasReviewWarnings;
            if (reviewWarningsShown) HistorySection.IsExpanded = true;
        };
        model.PropertyChanged += ModelChanged;
    }

    /// <summary>Refreshes every second and checkpoints at most five seconds of running work.</summary>
    private void Tick(object? sender, EventArgs e)
    {
        model.Refresh();
        if (++ticksSinceSave >= 5) { model.Save(); ticksSinceSave = 0; }
    }

    /// <summary>Creates a paused tracker after the common editor is confirmed.</summary>
    private void NewTracker(object sender, RoutedEventArgs e)
    {
        var editor = new TrackerEditor { Owner = this };
        if (editor.ShowDialog() == true) { model.Add(editor.TrackerLabel, editor.TrackerColor, editor.TrackerDescription); AdjustHeight(); }
    }

    /// <summary>Opens a context menu with mouse or keyboard using the same action button.</summary>
    private void OpenMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu!.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    /// <summary>Runs the shared editor for an existing card.</summary>
    private void EditTracker(object sender, RoutedEventArgs e)
    {
        var row = Row(sender);
        var editor = new TrackerEditor(row.Label, row.Color, row.Description) { Owner = this };
        if (editor.ShowDialog() == true) model.Edit(row.Id, editor.TrackerLabel, editor.TrackerColor, editor.TrackerDescription);
    }

    /// <summary>Starts or pauses one row while allowing deliberate overlapping timers.</summary>
    private void ToggleTracker(object sender, RoutedEventArgs e) => model.Toggle(Row(sender).Id);
    /// <summary>Finalizes the selected entry without deleting its total.</summary>
    private void EndTracker(object sender, RoutedEventArgs e) => model.End(Row(sender).Id);
    /// <summary>Archives a finished row, or restores a historical card for today without discarding saved time.</summary>
    private void ChangeArchiveState(object sender, RoutedEventArgs e)
    {
        var row = Row(sender);
        if (row.CanUnarchive)
        {
            model.Unarchive(row.Id);
            TrackerScrollArea.ScrollToTop();
        }
        else
        {
            model.Archive(row.Id);
            HistorySection.IsExpanded = true;
        }
        AdjustHeight();
    }
    /// <summary>Acknowledges a potentially forgotten midnight timer.</summary>
    private void ReviewTracker(object sender, RoutedEventArgs e) => model.MarkReviewed(Row(sender).Id);
    /// <summary>Copies the current duration as hours and minutes.</summary>
    private void CopyHours(object sender, RoutedEventArgs e) => CopyTotal(Row(sender).Id, false);
    /// <summary>Copies whole total minutes without rounding up.</summary>
    private void CopyMinutes(object sender, RoutedEventArgs e) => CopyTotal(Row(sender).Id, true);

    /// <summary>Handles clipboard contention without losing tracker state.</summary>
    private void CopyTotal(Guid id, bool minutesOnly)
    {
        try { Clipboard.SetText(model.CopyTotal(id, minutesOnly)); }
        catch (ExternalException) { MessageDialog.ShowMessage(this, "The clipboard is busy. Please try copying again.", "Time Tracker"); }
    }

    /// <summary>Requires explicit confirmation because deletion cannot be undone in the interface.</summary>
    private void DeleteTracker(object sender, RoutedEventArgs e)
    {
        var row = Row(sender);
        if (MessageDialog.ShowMessage(this, $"Delete {row.Label} and its recorded time?", "Delete tracker", "Delete"))
        { model.Delete(row.Id); AdjustHeight(); }
    }

    /// <summary>Applies the inline radio selection for mouse, keyboard, and UI Automation activation.</summary>
    private void ChangeSortOrder(object sender, RoutedEventArgs e) =>
        model.SetSortOrder(Enum.Parse<TrackerSortOrder>((string)((RadioButton)sender).Tag));

    /// <summary>Persists and immediately applies the chosen theme.</summary>
    private void ChangeTheme(object sender, RoutedEventArgs e)
    {
        var theme = (string)((MenuItem)sender).Tag;
        model.SavePreferences(model.Preferences with { Theme = theme });
        ThemeManager.Apply(theme);
    }

    /// <summary>Opens the clearly named local data folder through Windows Explorer.</summary>
    private void OpenDataFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(model.DataDirectory) { UseShellExecute = true });

    /// <summary>Resolves a row consistently for buttons and detached context menu items.</summary>
    private static TrackerRowViewModel Row(object sender) => (TrackerRowViewModel)((FrameworkElement)sender).DataContext;

    /// <summary>Stops timers on lock or session disconnection; unlock never restarts them.</summary>
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Dispatcher.Invoke(() => model.PauseAll());
    }

    /// <summary>Stops and saves before suspend; resume leaves timers paused.</summary>
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Dispatcher.Invoke(() => model.PauseAll());
    }

    /// <summary>Reevaluates system appearance when Windows personalization or accessibility changes.</summary>
    private void WindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => ThemeManager.Apply(model.Preferences.Theme));

    /// <summary>Saves before Windows logoff or shutdown without delaying the operating system.</summary>
    private void SessionEnding(object sender, SessionEndingCancelEventArgs e) => model.PauseAll();

    /// <summary>Adds a native hook to distinguish manual resizing from automatic row growth.</summary>
    private void SourceReady(object? sender, EventArgs e) =>
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);

    /// <summary>Remembers explicit user sizing, not programmatic adjustments or maximization.</summary>
    private IntPtr WindowMessage(IntPtr window, int message, IntPtr parameter, IntPtr data, ref bool handled)
    {
        const int sizing = 0x0214;
        if (message == sizing && ready && !automaticResize)
            model.SavePreferences(model.Preferences with { ManuallySized = true });
        return IntPtr.Zero;
    }

    /// <summary>Restores normal bounds, clamping them to a visible virtual desktop after monitor changes.</summary>
    private void RestorePlacement()
    {
        var preferences = model.Preferences;
        Width = Math.Clamp(preferences.Width, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
        Height = Math.Clamp(preferences.Height, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        if (preferences.Left is double left && preferences.Top is double top)
        {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        }
        if (preferences.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Grows with row count until the screen limit unless the user has chosen a size.</summary>
    private void AdjustHeight()
    {
        if (!ready || model.Preferences.ManuallySized || WindowState != WindowState.Normal) return;
        automaticResize = true;
        var rows = Math.Max(1, model.TodayRows.Count) + (HistorySection.IsExpanded ? model.EarlierRows.Count : 0);
        var visibleRows = model.TodayRows.Concat(HistorySection.IsExpanded ? model.EarlierRows : []).ToArray();
        var detailHeight = visibleRows.Count(row => row.NeedsReview) * 28;
        Height = Math.Clamp(280 + rows * 170 + detailHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40));
        if (Top + Height > SystemParameters.WorkArea.Bottom) Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height);
        automaticResize = false;
    }

    /// <summary>Adapts automatic height when retained history is expanded.</summary>
    private void HistoryExpanded(object sender, RoutedEventArgs e) => AdjustHeight();

    /// <summary>Makes midnight review warnings visible even when history had been collapsed.</summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasReviewWarnings))
        {
            if (model.HasReviewWarnings && !reviewWarningsShown) HistorySection.IsExpanded = true;
            reviewWarningsShown = model.HasReviewWarnings;
        }
        if (e.PropertyName == nameof(MainViewModel.IsEmpty)) AdjustHeight();
    }

    /// <summary>Offers a keyboard shortcut for the most frequent creation action.</summary>
    private void HandleShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        { NewTracker(sender, e); e.Handled = true; }
    }

    /// <summary>Stops timers and warns before closing if the final disk save fails.</summary>
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!model.PauseAll() && !MessageDialog.ShowMessage(this, "Your latest time could not be saved. Close anyway?", "Time Tracker — save failed", "Close anyway"))
        { e.Cancel = true; return; }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        model.SavePreferences(model.Preferences with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Maximized = WindowState == WindowState.Maximized });
    }

    /// <summary>Releases static event subscriptions so no disposed window receives Windows callbacks.</summary>
    private void WindowClosed(object? sender, EventArgs e)
    {
        timer.Stop();
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.UserPreferenceChanged -= WindowsPreferenceChanged;
        Application.Current.SessionEnding -= SessionEnding;
        model.PropertyChanged -= ModelChanged;
    }
}
