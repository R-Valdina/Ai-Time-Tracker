using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Presentation.Tests", Guid.NewGuid().ToString("N"));
            App? application = null;
            MainWindow? main = null;
            TrackerEditor? editor = null;
            MessageDialog? confirmation = null;
            try
            {
                application = new App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                main = new MainWindow(directory);
                main.Show();
                editor = new TrackerEditor { Owner = main };
                editor.Show();
                confirmation = new MessageDialog("Delete this test entry?", "Test confirmation", "Delete") { Owner = editor };
                confirmation.Show();

                // Round-trip themes while windows are open: existing dialogs must update, too.
                foreach (var theme in new[] { "Dark", "Light", "Dark" })
                {
                    ThemeManager.Apply(theme);
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

                    var settings = main.FindName("SettingsButton") as Button;
                    Assert.NotNull(settings);
                    VerifyMenu(settings!.ContextMenu!, settings);
                    var entry = new TrackerEntry(Guid.NewGuid(), "COLOR-1", TrackerColors.All[0], new DateOnly(2026, 10, 20), TimeSpan.Zero);
                    var row = new TrackerRowViewModel(entry, false, false);
                    var toggle = new Button { DataContext = row, Style = (Style)application.FindResource("TimerButton") };
                    toggle.ApplyTemplate();
                    DrainBindings();
                    Assert.Equal(Palette("StartSurface"), SolidColor(toggle.Background));
                    Assert.Equal(Palette("ActionText"), SolidColor(toggle.Foreground));
                    row.Refresh(entry, true);
                    DrainBindings();
                    Assert.Equal(Palette("StopSurface"), SolidColor(toggle.Background));
                    Assert.Equal(Palette("ActionText"), SolidColor(toggle.Foreground));
                    toggle.IsEnabled = false;
                    Assert.Equal(Palette("RowSurface"), SolidColor(toggle.Background));
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                confirmation?.Close();
                editor?.Close();
                main?.Close();
                application?.Shutdown();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF presentation checks did not complete.");
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

    /// <summary>Processes queued bindings and loaded handlers on the dedicated WPF STA thread.</summary>
    private static void DrainBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
