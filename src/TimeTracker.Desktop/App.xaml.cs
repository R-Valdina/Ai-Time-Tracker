using System.IO;
using System.Windows;

namespace TimeTracker.Desktop;

/// <summary>Owns the single-instance boundary and application startup.</summary>
public partial class App : Application
{
    private FileStream? instanceLock;

    /// <summary>Loads local data before displaying a window; rejects concurrent writers.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataDirectory = ResolveDataDirectory(e.Args);
            Directory.CreateDirectory(dataDirectory);
            try
            {
                instanceLock = new FileStream(Path.Combine(dataDirectory, "time-tracker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                MessageDialog.ShowMessage(null, "Time Tracker is already open, or its data folder is locked. Close the other instance before trying again.", "Time Tracker");
                Shutdown(1);
                return;
            }
            MainWindow = new MainWindow(dataDirectory);
            MainWindow.Show();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            MessageDialog.ShowMessage(null, exception.Message, "Time Tracker — unable to open data");
            Shutdown(1);
        }
    }

    /// <summary>Releases the data-directory lock after the window has saved its state.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        instanceLock?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Uses a clear per-user directory; automation can explicitly select an isolated folder.</summary>
    private static string ResolveDataDirectory(string[] arguments)
    {
        if (arguments.Length == 2 && arguments[0] == "--data-directory")
            return Path.GetFullPath(arguments[1]);
        if (arguments.Length != 0)
            throw new ArgumentException("Supported argument: --data-directory <folder>.");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");
    }
}
