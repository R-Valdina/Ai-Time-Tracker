using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
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
            Application? application = null;
            MainWindow? main = null;
            TrackerEditor? editor = null;
            MessageDialog? confirmation = null;
            try
            {
                Volatile.Write(ref phase, "initializing WPF resources");
                application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                    Resources = new ResourceDictionary
                    {
                        Source = new Uri("/TimeTracker;component/Themes/ApplicationResources.xaml", UriKind.Relative)
                    }
                };
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
                    Assert.Equal(3, application.Windows.Count);
                    var background = SystemParameters.HighContrast ? SystemColors.WindowColor :
                        (Color)ColorConverter.ConvertFromString(theme == "Dark" ? "#181818" : "#F5F7FB");
                    foreach (var window in new Window[] { main, editor, confirmation })
                    {
                        Assert.Equal(background, SolidColor(window.Background));
                        Assert.Equal(Palette("TextPrimary"), SolidColor(window.Foreground));
                    }

                    if (theme == "Dark" && !SystemParameters.HighContrast)
                    {
                        foreach (var key in new[] { "WindowSurface", "RowSurface", "RunningSurface", "ControlBorder", "ControlHover" })
                        {
                            var color = Palette(key);
                            Assert.Equal(color.R, color.G);
                            Assert.Equal(color.G, color.B);
                        }
                    }

                    foreach (var button in new[] { (Button)main.FindName("NewTrackerButton"), (Button)main.FindName("SettingsButton"), (Button)editor.FindName("SaveTrackerButton") })
                    {
                        Assert.Equal(Palette("RowSurface"), SolidColor(button.Background));
                        Assert.Equal(Palette("TextPrimary"), SolidColor(button.Foreground));
                        Assert.Equal("Segoe UI", button.FontFamily.Source);
                        Assert.Equal(14, button.FontSize);
                        Assert.Equal(FontWeights.SemiBold, button.FontWeight);
                        Assert.Null(button.Effect);
                        button.ApplyTemplate();
                        var surface = Assert.IsType<Border>(button.Template.FindName("Chrome", button));
                        Assert.Null(surface.Child);
                        var shadow = Assert.IsType<DropShadowEffect>(surface.Effect);
                        VerifyTextOutsideEffects(button);
                        Assert.InRange(shadow.ShadowDepth, 0.5, 1.5);
                        Assert.InRange(shadow.Opacity, 0.1, 0.2);
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
                    DrainBindings();
                    Assert.Equal(Palette("EndedSurface"), SolidColor(toggle.Background));
                    Assert.Equal(Palette("EndedBorder"), SolidColor(toggle.BorderBrush));
                    Assert.Equal(Palette("TextPrimary"), SolidColor(toggle.Foreground));
                    Assert.Equal(new Thickness(2), toggle.BorderThickness);
                    var chrome = Assert.IsType<Border>(toggle.Template.FindName("Chrome", toggle));
                    Assert.Equal(1, chrome.Opacity);
                    VerifyLabelForeground(toggle, "TextPrimary");

                    Volatile.Write(ref phase, $"checking stable {theme} row heights");
                    VerifyStableRowHeight(main);
                    Volatile.Write(ref phase, $"checking unchanged {theme} focused button borders");
                    VerifyUnchangedFocusBorder(confirmation);
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

    /// <summary>Checks that retained archived rows gain the new-day action when the calendar date changes.</summary>
    [Fact]
    public void ArchivedRowBecomesStartableOnTheFollowingDay()
    {
        var entry = new TrackerEntry(Guid.NewGuid(), "ARCHIVE-1", TrackerColors.All[0],
            new DateOnly(2026, 10, 20), TimeSpan.FromMinutes(8), IsEnded: true, IsArchived: true);
        var row = new TrackerRowViewModel(entry, false, false);
        Assert.False(row.CanToggle);
        Assert.False(row.CanArchive);
        row.Refresh(entry, false, isHistorical: true);
        Assert.True(row.IsHistorical);
        Assert.True(row.CanToggle);
        Assert.Equal("Start today", row.ToggleText);
        Assert.Equal("Start today timer for ARCHIVE-1", row.AccessibleToggleName);
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
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                Assert.Null(item.Effect);
                item.ApplyTemplate();
                var itemSurface = Assert.IsType<Border>(item.Template.FindName("MenuSurface", item));
                Assert.Null(itemSurface.Child);
                var shadow = Assert.IsType<DropShadowEffect>(itemSurface.Effect);
                VerifyTextOutsideEffects(item);
                Assert.InRange(shadow.ShadowDepth, 0.5, 1);
                Assert.InRange(shadow.Opacity, 0.05, 0.15);
                Assert.Equal(owner.FontFamily.Source, item.FontFamily.Source);
                Assert.Equal(owner.FontSize, item.FontSize);
                Assert.Equal(owner.FontWeight, item.FontWeight);
            }
        }
        finally { menu.IsOpen = false; }
    }

    /// <summary>Requires rendered labels to have no effect-bearing ancestor that would rasterize their glyphs.</summary>
    private static void VerifyTextOutsideEffects(Control control)
    {
        var labels = Descendants(control).Where(element => element is TextBlock or AccessText).ToArray();
        Assert.NotEmpty(labels);
        foreach (var label in labels)
        {
            for (DependencyObject? ancestor = label; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is UIElement element) Assert.Null(element.Effect);
                if (ReferenceEquals(ancestor, control)) break;
            }
        }
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
    private static void VerifyLabelForeground(DependencyObject parent, string paletteKey = "ActionText")
    {
        var text = Descendants(parent).Where(element => element is TextBlock or AccessText).ToArray();
        Assert.NotEmpty(text);
        foreach (var element in text)
            Assert.Equal(Palette(paletteKey), SolidColor((Brush)element.GetValue(TextElement.ForegroundProperty)));
    }

    /// <summary>Measures the live shared row before and after ending, including the app's automatic window sizing.</summary>
    private static void VerifyStableRowHeight(MainWindow window)
    {
        var model = (MainViewModel)window.DataContext;
        model.Add($"HEIGHT-{model.TodayRows.Count + 1}", TrackerColors.All[0]);
        DrainBindings();
        window.UpdateLayout();
        var row = model.TodayRows.Last();
        var card = Descendants(window).OfType<Border>().Single(border => border.Name == "TrackerCard" && ReferenceEquals(border.DataContext, row));
        var total = Descendants(card).OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "FinalTotal");
        Assert.Equal(Visibility.Hidden, total.Visibility);
        var rowHeight = card.ActualHeight;
        var windowHeight = window.ActualHeight;
        Assert.True(rowHeight >= 124, "Rows must reserve enough room for their final total.");

        const string description = "A short note explaining the investigation and the remaining acceptance checks for this card.";
        model.Edit(row.Id, row.Label, row.Color, description);
        DrainBindings();
        window.UpdateLayout();
        var note = Descendants(card).OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "TrackerDescriptionText");
        Assert.Equal(description, note.Text);
        Assert.Equal(rowHeight, card.ActualHeight, 3);
        Assert.True(note.ActualHeight >= 30, "Reserve two lines for optional descriptions.");
        Assert.True(note.TranslatePoint(new Point(0, 0), card).Y > total.TranslatePoint(new Point(0, 0), card).Y);
        model.End(row.Id);
        DrainBindings();
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, total.Visibility);
        Assert.Equal(rowHeight, card.ActualHeight, 3);
        Assert.Equal(windowHeight, window.ActualHeight, 3);
    }

    /// <summary>Focuses a live button and verifies that focus never changes its normal border color or width.</summary>
    private static void VerifyUnchangedFocusBorder(Window window)
    {
        var host = Assert.IsAssignableFrom<Panel>(window.Content);
        var button = new Button { Content = "Focus check" };
        host.Children.Add(button);
        try
        {
            window.Activate();
            button.Focus();
            DrainBindings();
            Assert.True(button.IsKeyboardFocused);
            var chrome = Assert.IsType<Border>(button.Template.FindName("Chrome", button));
            Assert.Equal(button.BorderThickness, chrome.BorderThickness);
            Assert.Equal(SolidColor(button.BorderBrush), SolidColor(chrome.BorderBrush));
            Assert.NotNull(button.FocusVisualStyle);
        }
        finally { host.Children.Remove(button); }
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

}
