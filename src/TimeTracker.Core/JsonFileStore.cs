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
            if (File.Exists(path))
            {
                // A corrupt primary must not overwrite the good backup during recovery.
                File.Replace(temporaryPath, path, recoveredFromBackup ? null : BackupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
                if (!File.Exists(BackupPath)) File.Copy(path, BackupPath);
            }
            recoveredFromBackup = false;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Reads and validates a complete document before returning it.</summary>
    private T Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var value = JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException("Empty JSON document.");
        validate?.Invoke(value);
        return value;
    }

    /// <summary>Limits recovery to expected file/format failures; programming errors still surface.</summary>
    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
}
