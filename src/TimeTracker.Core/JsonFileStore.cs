using System.Text.Json;

namespace TimeTracker.Core;

/// <summary>The recovered value and a visible explanation if the backup had to be used.</summary>
public sealed record LoadResult<T>(T Value, string? Warning);

/// <summary>Stores readable JSON with atomic replacement and a last-known-good backup.</summary>
/// <remarks>Use a single writer. A failed load must never be followed by overwriting that file.</remarks>
public sealed class JsonFileStore<T>(string path, Action<T>? validate = null)
{
    private readonly string path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private bool recoveredFromBackup;

    /// <summary>Gets the human-readable primary file path.</summary>
    public string FilePath => path;

    /// <summary>Gets the backup path beside the primary file.</summary>
    public string BackupPath => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".backup.json");

    /// <summary>Loads primary or backup; refuses silent data loss when neither existing file is readable.</summary>
    public LoadResult<T> Load(Func<T> createDefault)
    {
        if (!File.Exists(path) && !File.Exists(BackupPath)) return new(createDefault(), null);
        try { return new(Read(path), null); }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            try
            {
                var backup = Read(BackupPath);
                recoveredFromBackup = true;
                return new(backup, "Recovered the backup because the main data file could not be read. The recovered totals may be slightly older.");
            }
            catch (Exception backupException) when (IsReadFailure(backupException))
            {
                throw new InvalidDataException($"Cannot safely load {Path.GetFileName(path)} or its backup. Restore a valid file before continuing. Your files have not been replaced.", backupException);
            }
        }
    }

    /// <summary>Flushes a complete temporary file before atomically replacing the destination.</summary>
    public void Save(T value)
    {
        validate?.Invoke(value);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            // Keep backup maintenance separate: Windows ReplaceFile can briefly remove the primary name
            // while renaming it to the backup. Same-folder overwrite-rename keeps the primary available.
            if (File.Exists(path) && !recoveredFromBackup) SaveBackup();
            RenameAtomically(temporaryPath, path);
            if (!File.Exists(BackupPath)) SaveBackup();
            recoveredFromBackup = false;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Flushes a complete backup snapshot before atomically replacing the backup's filename.</summary>
    private void SaveBackup()
    {
        var temporaryBackup = BackupPath + ".tmp";
        try
        {
            File.Copy(path, temporaryBackup, overwrite: true);
            using (var stream = new FileStream(temporaryBackup, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Flush(flushToDisk: true);
            RenameAtomically(temporaryBackup, BackupPath);
        }
        finally
        {
            if (File.Exists(temporaryBackup)) File.Delete(temporaryBackup);
        }
    }

    /// <summary>Retries brief reader/antivirus sharing conflicts without ever truncating the destination.</summary>
    private static void RenameAtomically(string source, string destination)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(source, destination, overwrite: true); return; }
            catch (Exception exception) when (attempt < 9 && exception is IOException or UnauthorizedAccessException)
            {
                // Windows may retain a replaced file's old read handle briefly even with delete sharing.
                // Bound the wait to 90 ms; a persistent failure must reach the caller and retain its command.
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>Reads and validates a complete document before returning it.</summary>
    private T Read(string filePath)
    {
        // Permit atomic replacement while another process holds a read-only snapshot.
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var value = JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException("Empty JSON document.");
        validate?.Invoke(value);
        return value;
    }

    /// <summary>Limits recovery to expected file/format failures; programming errors still surface.</summary>
    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
}
