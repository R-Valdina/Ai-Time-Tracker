using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TimeTracker.Core;

namespace TimeTracker.Desktop;

/// <summary>Serializes local inbox files onto the UI dispatcher without dialogs, focus changes, or a second data writer.</summary>
public sealed class InboxWatcher : IDisposable
{
    private readonly string directory;
    private readonly string logPath;
    private readonly Action<InboxCommand> apply;
    private readonly DispatcherTimer timer;
    private readonly ConcurrentQueue<string> arrivals = new();
    private readonly ConcurrentQueue<string> watcherErrors = new();
    private readonly Queue<PendingFile> pending = new();
    private readonly HashSet<string> queued = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private int scans;
    private bool disposed;

    /// <summary>Queues existing files by creation time, then receives new files in watcher arrival order.</summary>
    public InboxWatcher(string dataDirectory, Dispatcher dispatcher, Action<InboxCommand> apply)
    {
        this.apply = apply;
        directory = Path.Combine(dataDirectory, "inbox");
        logPath = Path.Combine(dataDirectory, "time-tracker.log");
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, ProcessPending, dispatcher);
        try
        {
            Directory.CreateDirectory(directory);
            DiscoverFiles();
            watcher = new FileSystemWatcher(directory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false
            };
            watcher.Created += FileArrived;
            watcher.Renamed += FileArrived;
            watcher.Error += WatcherFailed;
            watcher.EnableRaisingEvents = true;
            DiscoverFiles(); // Recover files created between the initial scan and enabling events.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Log($"Inbox setup deferred: {exception.Message}"); }
    }

    /// <summary>Receives only notifications on the worker thread; file access and commands stay on the UI thread.</summary>
    private void FileArrived(object sender, FileSystemEventArgs e) => arrivals.Enqueue(e.FullPath);

    /// <summary>Records notification loss for the UI thread; periodic discovery recovers missing events.</summary>
    private void WatcherFailed(object sender, ErrorEventArgs e) => watcherErrors.Enqueue(e.GetException().Message);

    /// <summary>Processes bounded work per tick and keeps an unfinished head command ahead of later arrivals.</summary>
    private void ProcessPending(object? sender, EventArgs e)
    {
        if (disposed) return;
        while (watcherErrors.TryDequeue(out var error)) Log($"Inbox watcher: {error}");
        while (arrivals.TryDequeue(out var path)) Enqueue(path);
        if (++scans % 8 == 0)
        {
            try { Directory.CreateDirectory(directory); DiscoverFiles(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Log($"Inbox scan deferred: {exception.Message}"); }
        }
        for (var count = 0; count < 16 && pending.TryPeek(out var file); count++)
        {
            if (!Process(file)) break;
            pending.Dequeue();
            queued.Remove(file.Path);
        }
    }

    /// <summary>Recovers startup/backlogged files with a deterministic filename tie-breaker for equal creation times.</summary>
    private void DiscoverFiles()
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(File.GetCreationTimeUtc).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            Enqueue(path);
    }

    /// <summary>Deduplicates watcher and recovery notifications while retaining their queue position.</summary>
    private void Enqueue(string path)
    {
        if (queued.Add(path)) pending.Enqueue(new PendingFile(path));
    }

    /// <summary>Retries locked or incomplete files briefly, then rejects invalid commands; save failures retain the command.</summary>
    private bool Process(PendingFile file)
    {
        InboxCommand command;
        try
        {
            // Disallow a concurrent writer during the read. Producers should rename a completed .tmp file to .json.
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 65536) throw new InvalidDataException("Command files must be no larger than 64 KiB.");
            using var reader = new StreamReader(stream);
            command = InboxCommand.Parse(reader.ReadToEnd());
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (++file.ReadAttempts < 8) return false;
            return Reject(file, exception.Message);
        }
        try { apply(command); }
        catch (Exception exception) when (exception is InvalidDataException or KeyNotFoundException or InvalidOperationException)
        { return Reject(file, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!file.SaveFailureLogged) Log($"Inbox save deferred for {Path.GetFileName(file.Path)}: {exception.Message}");
            file.SaveFailureLogged = true;
            return false; // A retry must durably save even if its command is already applied in memory.
        }
        try { File.Delete(file.Path); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!file.SaveFailureLogged) Log($"Inbox deletion deferred for {Path.GetFileName(file.Path)}: {exception.Message}");
            file.SaveFailureLogged = true;
            return false;
        }
    }

    /// <summary>Moves a rejected command and writes a paired reason file without overwriting any previous rejection.</summary>
    private bool Reject(PendingFile file, string reason)
    {
        reason = reason.Replace('\r', ' ').Replace('\n', ' ');
        if (reason.Length > 240) reason = reason[..240];
        try
        {
            var rejected = Path.Combine(directory, "rejected");
            Directory.CreateDirectory(rejected);
            var target = Path.Combine(rejected, Path.GetFileNameWithoutExtension(file.Path) + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(target + ".txt", reason);
            try { File.Move(file.Path, target); }
            catch { File.Delete(target + ".txt"); throw; }
            Log($"Rejected {Path.GetFileName(file.Path)}: {reason}");
            return true;
        }
        catch (FileNotFoundException) { return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!file.RejectionFailureLogged) Log($"Inbox rejection deferred: {exception.Message}");
            file.RejectionFailureLogged = true;
            return false;
        }
    }

    /// <summary>Writes local operational messages on a best-effort basis; log failures never interrupt the UI.</summary>
    private void Log(string message)
    {
        try { File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Stops processing before the main window releases the app's single-writer lock.</summary>
    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        watcher?.Dispose();
    }

    /// <summary>Retains retry state for the queue head without letting a later file overtake it.</summary>
    private sealed class PendingFile(string path)
    {
        public string Path { get; } = path;
        public int ReadAttempts { get; set; }
        public bool SaveFailureLogged { get; set; }
        public bool RejectionFailureLogged { get; set; }
    }
}
