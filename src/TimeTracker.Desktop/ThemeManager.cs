using System.Windows;
using Microsoft.Win32;

namespace TimeTracker.Desktop;

/// <summary>Swaps shared palette resources while leaving row accent colors intact.</summary>
public static class ThemeManager
{
    /// <summary>Applies Light, Dark, or the current Windows app preference. High contrast retains system colors.</summary>
    public static void Apply(string preference)
    {
        var dark = preference == "Dark" || preference == "System" && WindowsUsesDarkMode();
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        if (SystemParameters.HighContrast)
        {
            var resources = Application.Current.Resources;
            resources["WindowSurface"] = SystemColors.WindowBrush;
            resources["RowSurface"] = SystemColors.WindowBrush;
            resources["RunningSurface"] = SystemColors.HighlightBrush;
            resources["TextPrimary"] = SystemColors.WindowTextBrush;
            resources["TextSecondary"] = SystemColors.WindowTextBrush;
            resources["ControlBorder"] = SystemColors.WindowTextBrush;
            resources["ControlHover"] = SystemColors.HighlightBrush;
            resources["ActionSurface"] = SystemColors.HighlightBrush;
            resources["ActionText"] = SystemColors.HighlightTextBrush;
            resources["WarningText"] = SystemColors.WindowTextBrush;
            resources["DangerText"] = SystemColors.WindowTextBrush;
        }
        else
        {
            // Remove high-contrast overrides so the merged theme can supply colors again.
            foreach (var key in new[] { "WindowSurface", "RowSurface", "RunningSurface", "TextPrimary", "TextSecondary", "ControlBorder", "ControlHover", "ActionSurface", "ActionText", "WarningText", "DangerText" })
                Application.Current.Resources.Remove(key);
        }
    }

    /// <summary>Reads Windows' documented user preference without requiring administrator privileges.</summary>
    private static bool WindowsUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
