using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TimeTracker.Desktop;

/// <summary>Swaps shared palette resources while leaving row accent colors intact.</summary>
public static class ThemeManager
{
    private static bool usesDarkPalette;

    /// <summary>Applies Light, Dark, or the current Windows app preference. High contrast retains system colors.</summary>
    public static void Apply(string preference)
    {
        usesDarkPalette = preference == "Dark" || preference == "System" && WindowsUsesDarkMode();
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/TimeTracker;component/Themes/{(usesDarkPalette ? "Dark" : "Light")}.xaml", UriKind.Relative)
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
            resources["StartSurface"] = SystemColors.HighlightBrush;
            resources["StopSurface"] = SystemColors.HighlightBrush;
            resources["EndedSurface"] = SystemColors.WindowBrush;
            resources["EndedBorder"] = SystemColors.WindowTextBrush;
            resources["WarningText"] = SystemColors.WindowTextBrush;
            resources["DangerText"] = SystemColors.WindowTextBrush;
        }
        else
        {
            // Remove high-contrast overrides so the merged theme can supply colors again.
            foreach (var key in new[] { "WindowSurface", "RowSurface", "RunningSurface", "TextPrimary", "TextSecondary", "ControlBorder", "ControlHover", "ActionSurface", "ActionText", "StartSurface", "StopSurface", "EndedSurface", "EndedBorder", "WarningText", "DangerText" })
                Application.Current.Resources.Remove(key);
        }
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
    }

    /// <summary>Applies native title-bar colors when a main, editor, or message window receives its handle.</summary>
    public static void InitializeWindow(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
        ApplyTitleBar(window);
    }

    /// <summary>Uses supported Windows chrome attributes; older Windows versions ignore unsupported colors.</summary>
    private static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        uint immersiveDark = usesDarkPalette && !SystemParameters.HighContrast ? 1u : 0u;
        if (DwmSetWindowAttribute(handle, 20, ref immersiveDark, sizeof(uint)) != 0)
            DwmSetWindowAttribute(handle, 19, ref immersiveDark, sizeof(uint));
        var captionColor = SystemParameters.HighContrast ? uint.MaxValue : ColorReference("WindowSurface");
        var textColor = SystemParameters.HighContrast ? uint.MaxValue : ColorReference("TextPrimary");
        DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(uint));
        DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(uint));
    }

    /// <summary>Converts a shared palette brush into the COLORREF format used by Windows.</summary>
    private static uint ColorReference(string key)
    {
        var color = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        return (uint)(color.R | color.G << 8 | color.B << 16);
    }

    /// <summary>Sets a native caption attribute without replacing Windows resizing or accessibility behavior.</summary>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);

    /// <summary>Reads Windows' documented user preference without requiring administrator privileges.</summary>
    private static bool WindowsUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
