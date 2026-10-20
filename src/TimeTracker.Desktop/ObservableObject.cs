using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimeTracker.Desktop;

/// <summary>Provides WPF property notifications without a third-party MVVM dependency.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>Notifies bindings when a displayed property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Refreshes one named property in the interface.</summary>
    protected void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
