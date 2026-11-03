using System.Text.Json;

namespace TimeTracker.Core;

/// <summary>A local command submitted by an external reader; only the app changes tracker data.</summary>
public sealed record InboxCommand(string Command, Guid RowId, string? TimecardId = null, DateTimeOffset? PostedAt = null)
{
    /// <summary>Parses the command envelope and ignores posting fields for end commands.</summary>
    public static InboxCommand Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Command", out var action) || action.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("RowId", out var row) || row.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(row.GetString(), out var id))
            throw new InvalidDataException("Command and a valid RowId are required.");
        var name = action.GetString()!;
        string? timecardId = null;
        DateTimeOffset? postedAt = null;
        if (name == "complete")
        {
            if (!root.TryGetProperty("TimecardId", out var timecard) || timecard.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("PostedAt", out var posted) || posted.ValueKind != JsonValueKind.String ||
                !posted.TryGetDateTimeOffset(out var timestamp))
                throw new InvalidDataException("Complete requires a TimecardId and an ISO date-time PostedAt.");
            timecardId = timecard.GetString();
            postedAt = timestamp;
        }
        var command = new InboxCommand(name, id, timecardId, postedAt);
        command.Validate();
        return command;
    }

    /// <summary>Protects the engine when a caller constructs a command directly rather than parsing JSON.</summary>
    public void Validate()
    {
        if (Command is not ("end" or "complete")) throw new InvalidDataException("Unknown command; use end or complete.");
        if (RowId == Guid.Empty) throw new InvalidDataException("RowId must identify an existing tracker.");
        if (Command == "complete" && (string.IsNullOrWhiteSpace(TimecardId) || PostedAt is null))
            throw new InvalidDataException("Complete requires a nonempty TimecardId and PostedAt.");
    }
}
