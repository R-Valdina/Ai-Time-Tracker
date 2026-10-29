using System.Windows;
using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Shares the same focused, themed duration editor for adding and subtracting time.</summary>
public partial class TimeAdjustmentDialog : Window
{
    private readonly Action<TimeSpan> apply;

    /// <summary>Defers all changes until valid input is confirmed with Enter or OK.</summary>
    public TimeAdjustmentDialog(string label, bool subtract, Action<TimeSpan> apply)
    {
        this.apply = apply;
        InitializeComponent();
        ThemeManager.InitializeWindow(this);
        Title = subtract ? "Subtract time" : "Add time";
        CardHeading.Text = $"{Title}: {label}";
        SubtractHint.Visibility = subtract ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => DurationInput.Focus();
    }

    /// <summary>Keeps invalid input open and handles totals too large for a stored duration without losing data.</summary>
    private void SaveAdjustment(object sender, RoutedEventArgs e)
    {
        if (!TimeAdjustment.TryParse(DurationInput.Text, out var duration))
        {
            ValidationMessage.Text = "Enter positive whole minutes or hours:minutes (minutes 00–59).";
            DurationInput.Focus();
            return;
        }
        try { apply(duration); }
        catch (OverflowException)
        {
            ValidationMessage.Text = "That amount would make the total too large. Enter a smaller amount.";
            DurationInput.Focus();
            return;
        }
        DialogResult = true;
    }
}
