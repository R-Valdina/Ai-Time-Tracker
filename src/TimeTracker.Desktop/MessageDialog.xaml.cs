using System.Windows;

namespace TimeTracker.Desktop;

/// <summary>A shared, theme-aware message and confirmation dialog with a safe cancel default.</summary>
public partial class MessageDialog : Window
{
    /// <summary>Builds an informational message or an explicit confirmation without exposing native light-only chrome.</summary>
    public MessageDialog(string message, string title, string? confirmationLabel = null)
    {
        InitializeComponent();
        ThemeManager.InitializeWindow(this);
        MessageText.Text = message;
        Title = title;
        CancelButton.Visibility = confirmationLabel is null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmationLabel ?? "OK";
        ConfirmButton.IsDefault = confirmationLabel is null;
        Loaded += (_, _) =>
        {
            if (confirmationLabel is null) ConfirmButton.Focus();
            else CancelButton.Focus();
        };
    }

    /// <summary>Shows a message using the current theme; confirmation returns true only on explicit acceptance.</summary>
    public static bool ShowMessage(Window? owner, string message, string title, string? confirmationLabel = null)
    {
        var dialog = new MessageDialog(message, title, confirmationLabel);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Closes with an affirmative result; Escape and closing the dialog retain the safe negative result.</summary>
    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
