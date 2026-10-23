using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimeTracker.Desktop;

/// <summary>Shares post-mouse-action focus cleanup while preserving keyboard navigation and screen-reader focus.</summary>
public static class WindowInteraction
{
    /// <summary>Enables focus cleanup after a button's own action handlers have finished.</summary>
    public static readonly DependencyProperty ReleaseFocusAfterActionProperty = DependencyProperty.RegisterAttached(
        "ReleaseFocusAfterAction", typeof(bool), typeof(WindowInteraction), new PropertyMetadata(false, ConfigureWindow));

    private static readonly DependencyProperty MenuRegisteredProperty = DependencyProperty.RegisterAttached(
        "MenuRegistered", typeof(bool), typeof(WindowInteraction), new PropertyMetadata(false));

    private static readonly DependencyProperty PointerActionProperty = DependencyProperty.RegisterAttached(
        "PointerAction", typeof(bool), typeof(WindowInteraction), new PropertyMetadata(false));

    /// <summary>Gets whether a window releases completed action buttons from keyboard and logical focus.</summary>
    public static bool GetReleaseFocusAfterAction(DependencyObject element) => (bool)element.GetValue(ReleaseFocusAfterActionProperty);

    /// <summary>Enables the shared behavior through the application window style.</summary>
    public static void SetReleaseFocusAfterAction(DependencyObject element, bool value) => element.SetValue(ReleaseFocusAfterActionProperty, value);

    /// <summary>Listens at the window level so modal dialogs and validation finish before cleanup is scheduled.</summary>
    private static void ConfigureWindow(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not Window window) return;
        var handler = new RoutedEventHandler(ButtonActionCompleted);
        var pointerHandler = new MouseButtonEventHandler(PointerReleased);
        if ((bool)change.NewValue)
        {
            window.AddHandler(Mouse.PreviewMouseUpEvent, pointerHandler, handledEventsToo: true);
            window.AddHandler(ButtonBase.ClickEvent, handler, handledEventsToo: true);
        }
        else
        {
            window.RemoveHandler(Mouse.PreviewMouseUpEvent, pointerHandler);
            window.RemoveHandler(ButtonBase.ClickEvent, handler);
        }
    }

    /// <summary>Registers menu cleanup once and defers focus changes until the current action has returned.</summary>
    private static void ButtonActionCompleted(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ButtonBase button) return;
        var pointerAction = (bool)button.GetValue(PointerActionProperty);
        button.ClearValue(PointerActionProperty);
        if (button.ContextMenu is { } menu)
        {
            if (!(bool)menu.GetValue(MenuRegisteredProperty))
            {
                menu.SetValue(MenuRegisteredProperty, true);
                menu.Opened += MenuOpened;
                menu.Closed += MenuClosed;
                menu.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PointerReleased), handledEventsToo: true);
                menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(MenuActionCompleted), handledEventsToo: true);
            }
            menu.SetValue(PointerActionProperty, pointerAction);
        }
        if (pointerAction) ScheduleFocusRelease(button);
    }

    /// <summary>Records mouse activation before handlers can open a modal dialog or move focus to another control.</summary>
    private static void PointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is ContextMenu menu) { menu.SetValue(PointerActionProperty, true); return; }
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is ButtonBase button) { button.SetValue(PointerActionProperty, true); return; }
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
    }

    /// <summary>Starts each menu visit with a fresh input state, including menus opened by keyboard shortcuts.</summary>
    private static void MenuOpened(object sender, RoutedEventArgs e) => ((ContextMenu)sender).ClearValue(PointerActionProperty);

    /// <summary>Runs after menu-item actions, including an editor dialog opened from the menu.</summary>
    private static void MenuActionCompleted(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        ReleaseMenuOwner(menu);
        menu.ClearValue(PointerActionProperty);
    }

    /// <summary>Clears the focus that WPF would otherwise restore to the menu's opening button.</summary>
    private static void MenuClosed(object sender, RoutedEventArgs e) => ReleaseMenuOwner((ContextMenu)sender);

    /// <summary>Finds the opening button without retaining references to unrelated windows.</summary>
    private static void ReleaseMenuOwner(ContextMenu menu)
    {
        if ((bool)menu.GetValue(PointerActionProperty) && menu.PlacementTarget is ButtonBase button)
            ScheduleFocusRelease(button);
    }

    /// <summary>Releases only the initiating button; validation fields and open-menu navigation retain their focus.</summary>
    private static void ScheduleFocusRelease(ButtonBase button) => button.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        if (button.ContextMenu is { IsOpen: true }) return;
        var hadKeyboardFocus = button.IsKeyboardFocusWithin;
        var scope = FocusManager.GetFocusScope(button);
        if (ReferenceEquals(FocusManager.GetFocusedElement(scope), button)) FocusManager.SetFocusedElement(scope, null);
        if (hadKeyboardFocus) Keyboard.ClearFocus();
    });
}
