using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TimeTracker.Core;
using TimeTracker.Desktop;

namespace TimeTracker.Ui.Tests;

/// <summary>Checks actual WPF window/control properties, beyond merely persisting a theme preference.</summary>
public sealed class PresentationTests
{
    /// <summary>Prevents recurrence of white windows, default menu gutters, blue creation buttons, and incorrect timer colors.</summary>
    [Fact]
    public void ThemesReachWindowsDialogsMenusAndButtonStates()
    {
        Exception? failure = null;
        var phase = "starting the WPF test thread";
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Presentation.Tests", Guid.NewGuid().ToString("N"));
            App? application = null;
            MainWindow? main = null;
            TrackerEditor? editor = null;
            MessageDialog? confirmation = null;
            try
            {
                Volatile.Write(ref phase, "initializing WPF resources");
                application = new PresentationApplication();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Volatile.Write(ref phase, "opening the main window");
                main = new MainWindow(directory);
                main.Show();
                Volatile.Write(ref phase, "opening the tracker editor");
                editor = new TrackerEditor { Owner = main };
                editor.Show();
                Volatile.Write(ref phase, "opening the confirmation dialog");
                confirmation = new MessageDialog("Delete this test entry?", "Test confirmation", "Delete") { Owner = editor };
                confirmation.Show();

                // Round-trip themes while windows are open: existing dialogs must update, too.
                foreach (var theme in new[] { "Dark", "Light", "Dark" })
                {
                    Volatile.Write(ref phase, $"applying the {theme} theme");
                    SelectTheme(main, theme);
                    Volatile.Write(ref phase, $"processing {theme} theme bindings");
                    DrainBindings();
                    var background = SystemParameters.HighContrast ? SystemColors.WindowColor :
                        (Color)ColorConverter.ConvertFromString(theme == "Dark" ? "#141A24" : "#F5F7FB");
                    foreach (var window in new Window[] { main, editor, confirmation })
                    {
                        Assert.Equal(background, SolidColor(window.Background));
                        Assert.Equal(Palette("TextPrimary"), SolidColor(window.Foreground));
                    }

                    foreach (var button in new[] { (Button)main.FindName("NewTrackerButton"), (Button)editor.FindName("SaveTrackerButton") })
                    {
                        Assert.Equal(Palette("RowSurface"), SolidColor(button.Background));
                        Assert.Equal(Palette("TextPrimary"), SolidColor(button.Foreground));
                        Assert.IsType<DropShadowEffect>(button.Effect);
                    }

                    Volatile.Write(ref phase, $"checking the {theme} menu");
                    var settings = main.FindName("SettingsButton") as Button;
                    Assert.NotNull(settings);
                    VerifyMenu(settings!.ContextMenu!, settings);
                    Volatile.Write(ref phase, $"checking {theme} timer button states");
                    var entry = new TrackerEntry(Guid.NewGuid(), "COLOR-1", TrackerColors.All[0], new DateOnly(2026, 10, 20), TimeSpan.Zero);
                    var row = new TrackerRowViewModel(entry, false, false);
                    var toggle = new Button { Content = "Start", DataContext = row, Style = (Style)application.FindResource("TimerButton") };
                    toggle.ApplyTemplate();
                    toggle.Measure(new Size(120, 40));
                    toggle.Arrange(new Rect(0, 0, 120, 40));
                    toggle.UpdateLayout();
                    DrainBindings();
                    Assert.Equal(Palette("StartSurface"), SolidColor(toggle.Background));
                    Assert.Equal(Palette("ActionText"), SolidColor(toggle.Foreground));
                    VerifyLabelForeground(toggle);
                    row.Refresh(entry, true);
                    DrainBindings();
                    Assert.Equal(Palette("StopSurface"), SolidColor(toggle.Background));
                    Assert.Equal(Palette("ActionText"), SolidColor(toggle.Foreground));
                    VerifyLabelForeground(toggle);
                    toggle.IsEnabled = false;
                    Assert.Equal(Palette("RowSurface"), SolidColor(toggle.Background));
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref phase, "closing the presentation windows");
                confirmation?.Close();
                editor?.Close();
                main?.Close();
                Volatile.Write(ref phase, "shutting down the WPF dispatcher");
                application?.Shutdown();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"WPF presentation checks did not complete while {Volatile.Read(ref phase)}.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Checks that finalizing an entry notifies bindings and supplies both timecard formats.</summary>
    [Fact]
    public void EndingEntryRefreshesVisibleFinalTotal()
    {
        var entry = new TrackerEntry(Guid.NewGuid(), "END-1", TrackerColors.All[0], new DateOnly(2026, 10, 20), TimeSpan.Zero);
        var row = new TrackerRowViewModel(entry, true, false);
        var notifications = new List<string?>();
        row.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        row.Refresh(entry with { IsEnded = true, Elapsed = TimeSpan.FromSeconds(5076) }, false);
        Assert.True(row.IsEnded);
        Assert.False(row.CanToggle);
        Assert.Equal("Total: 1 h 24 min · 84 min", row.FinalTotal);
        Assert.Contains(nameof(row.IsEnded), notifications);
        Assert.Contains(nameof(row.FinalTotal), notifications);
    }

    /// <summary>Verifies the rounded, themed menu surface and its gutter-free full-width scroll container.</summary>
    private static void VerifyMenu(ContextMenu menu, Button owner)
    {
        menu.PlacementTarget = owner;
        menu.IsOpen = true;
        try
        {
            DrainBindings();
            menu.ApplyTemplate();
            var surface = Assert.IsType<Border>(menu.Template.FindName("MenuSurface", menu));
            Assert.Equal(new CornerRadius(7), surface.CornerRadius);
            Assert.Equal(Palette("RowSurface"), SolidColor(surface.Background));
            Assert.IsType<ScrollViewer>(surface.Child);
            Assert.True(menu.ActualWidth > 100);
        }
        finally { menu.IsOpen = false; }
    }

    /// <summary>Gets a palette color from the same application resources used by all open windows.</summary>
    private static Color Palette(string key) => SolidColor((Brush)Application.Current.FindResource(key));

    /// <summary>Requires a concrete brush so unresolved window/control styles fail explicitly.</summary>
    private static Color SolidColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    /// <summary>Exercises the real theme command so subsequent Windows notifications honor the selected preference.</summary>
    private static void SelectTheme(MainWindow window, string theme)
    {
        var settings = (Button)window.FindName("SettingsButton");
        var item = settings.ContextMenu!.Items.OfType<MenuItem>().Single(candidate => Equals(candidate.Tag, theme));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    /// <summary>Checks rendered label elements, which can override a correct Button.Foreground through implicit text styles.</summary>
    private static void VerifyLabelForeground(DependencyObject parent)
    {
        var text = Descendants(parent).Where(element => element is TextBlock or AccessText).ToArray();
        Assert.NotEmpty(text);
        foreach (var element in text)
            Assert.Equal(Palette("ActionText"), SolidColor((Brush)element.GetValue(TextElement.ForegroundProperty)));
    }

    /// <summary>Enumerates a small control's visual tree to inspect its actual rendered label.</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>Processes bindings, layout, and loaded handlers without waiting for the desktop to become idle.</summary>
    private static void DrainBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>Reuses the real application resources without launching production startup inside the test runner.</summary>
    private sealed class PresentationApplication : App
    {
        /// <summary>Suppresses the queued startup callback, which would otherwise interpret test-host arguments and open unrelated windows.</summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            // This fixture explicitly owns all windows and uses only its temporary data directory.
        }
    }
}
