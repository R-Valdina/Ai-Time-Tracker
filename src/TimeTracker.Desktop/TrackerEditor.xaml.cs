using System.Windows;
using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Shared create/edit dialog with a required card reference, accent color, and optional short description.</summary>
public partial class TrackerEditor : Window
{
    /// <summary>Initializes a new or existing tracker without modifying the tracker until Save.</summary>
    public TrackerEditor(string label = "", string? color = null, string description = "")
    {
        InitializeComponent();
        ThemeManager.InitializeWindow(this);
        var names = new[] { "Blue — feature", "Red — bug", "Purple — publishing", "Green", "Orange", "Teal" };
        ColorInput.ItemsSource = TrackerColors.All.Select((value, index) => new ColorChoice(names[index], value)).ToArray();
        ColorInput.SelectedIndex = Math.Max(0, TrackerColors.All.ToList().IndexOf(color ?? TrackerColors.All[0]));
        LabelInput.Text = label;
        DescriptionInput.Text = description;
        Title = label.Length == 0 ? "New tracker" : "Edit tracker";
        Loaded += (_, _) => { LabelInput.Focus(); LabelInput.SelectAll(); };
    }

    /// <summary>Gets the trimmed card reference after successful confirmation.</summary>
    public string TrackerLabel => LabelInput.Text.Trim();
    /// <summary>Gets the optional trimmed note after successful confirmation.</summary>
    public string TrackerDescription => DescriptionInput.Text.Trim();
    /// <summary>Gets the explicitly selected accent after successful confirmation.</summary>
    public string TrackerColor => ((ColorChoice)ColorInput.SelectedItem).Color;

    /// <summary>Validates required input before closing so no empty tracker can be created.</summary>
    private void SaveTracker(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TrackerLabel) || ColorInput.SelectedItem is null)
        {
            ValidationMessage.Text = "Enter a card reference and choose a color.";
            LabelInput.Focus();
            return;
        }
        DialogResult = true;
    }

    /// <summary>Provides an accessible color name without persisting a fixed work-item type.</summary>
    private sealed record ColorChoice(string Name, string Color);
}
