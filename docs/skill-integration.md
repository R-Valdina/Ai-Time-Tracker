# Local skill integration

Time Tracker remains offline and is the **only writer** of `tracker-data.json` and its backup. An external skill may read the data file and publish command files into the inbox. It must never edit the tracker data, backup, preferences, or lock file while the app is open. No network, accounts, services, or additional runtime packages are involved.

## Data and migration

The default data directory is `%LOCALAPPDATA%\TimeTracker`. A custom `--data-directory` uses the same layout in that folder.

- `tracker-data.json`: version 2 entries, including nullable `TimecardId` (string) and `PostedAt` (ISO date-time with offset).
- `tracker-data.backup.json`: previous good snapshot. Startup migration preserves the version 1 primary here.
- `time-tracker.lock`: the app's existing single-writer boundary; leave it alone.
- `inbox\`: command files, created automatically and scanned at startup.
- `inbox\rejected\`: rejected command files with paired `.json.txt` reasons.
- `time-tracker.log`: local rejection and inbox I/O messages; logging never opens a dialog.

A version 1 file loads with the same entry IDs, labels, colors, dates, elapsed totals, descriptions, and flags. The two missing posting fields become null. Startup then atomically saves version 2; subsequent saves also use version 2. The label remains free text, with the same existing editor limits and no card-number format validation.

For example, this version 1 data:

```json
{
  "Version": 1,
  "Entries": [{
    "Id": "4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8",
    "Label": "Any free-text card reference",
    "Color": "#2563EB",
    "Date": "2026-11-02",
    "Elapsed": "01:30:00",
    "IsEnded": true,
    "NeedsReview": false,
    "IsArchived": false,
    "Description": "Testing notes"
  }]
}
```

becomes the same document with `"Version": 2` and these additional fields on the entry:

```json
"TimecardId": null,
"PostedAt": null
```

Earlier app binaries that only support version 1 cannot open version 2. Keep the backup for recovery; do not downgrade by editing an open app's data file.

## Commands

Use the entry's **Id**, not its Label. IDs remain stable through edits and sorting. Each command file contains one JSON object; property names and command names are case-sensitive. Unknown extra properties are ignored. Command files are limited to 64 KiB.

To freeze the total without archiving or filing, publish an `end` command:

```json
{
  "Command": "end",
  "RowId": "4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8"
}
```

To mark a successful external timecard posting, end, stamp, and archive in **one save**, publish `complete`:

```json
{
  "Command": "complete",
  "RowId": "4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8",
  "TimecardId": "TC-2026-0042",
  "PostedAt": "2026-11-03T12:30:00-05:00"
}
```

`complete` requires a nonblank string `TimecardId` and an ISO date-time `PostedAt`. A Guid is supplied as a string. `end` ignores both posting fields.

- Repeated `end` succeeds without changing an already-ended total.
- Repeated `complete` with exactly the same TimecardId succeeds without changing the original posting timestamp or total.
- A different TimecardId on an already-filed entry is rejected; it is never overwritten.
- Unknown rows, unknown commands, invalid JSON, and invalid required fields are rejected.
- No command changes keyboard focus or opens a popup.

## Publish files safely

Write a complete command to a unique `.tmp` file **inside the inbox**, close it, then rename it to `.json`. Never reuse or modify a submitted `.json` file. For example, in PowerShell:

```powershell
$inboxDirectory = Join-Path $env:LOCALAPPDATA 'TimeTracker\inbox'
New-Item -ItemType Directory -Force $inboxDirectory | Out-Null
$commandPath = Join-Path $inboxDirectory (([guid]::NewGuid().ToString('N')) + '.json')
$temporaryPath = $commandPath + '.tmp'
@{
    Command = 'end'
    RowId = '4ef4da84-a52e-41c8-bd33-0bb7a8bf13e8'
} | ConvertTo-Json | Set-Content -LiteralPath $temporaryPath -Encoding utf8
Move-Item -LiteralPath $temporaryPath -Destination $commandPath
```

The running app processes arrivals serially on its UI dispatcher. Startup/recovery scans order existing files by creation time, then filename for ties. Locked or incomplete files are retried for approximately two seconds; a pending head command prevents later commands from overtaking it. Periodic scans recover missed watcher notifications. Commands submitted while the app is closed wait until startup.

A successfully applied command is saved immediately and only then deleted. If saving fails, its file remains queued for retry—even when the change is already present in memory. If the app exits before acknowledgment, the retained file is safe to replay. Rejection moves the original file to `inbox\rejected\<name>-<unique-id>.json`, writes a short reason beside it as `.json.txt`, and logs the reason. Rejections never show a popup.

For acknowledgment, wait for deletion and verify the row in the saved data, or inspect the rejected folder and reason. Do not resubmit a conflicting ID; correct the external request first.

The store flushes a complete same-folder temporary snapshot, then atomically replaces the primary while maintaining the backup. Readers should close files promptly and, when supported, open with `FileShare.ReadWrite | FileShare.Delete` so Windows permits replacement during a read. An opened reader sees the complete old or new snapshot, never partially written JSON. Windows can briefly reject opening the primary during replacement (missing-path, access, or sharing errors); retry opening with a short delay and a bounded timeout, such as two seconds. Do not treat a transient open failure as an empty tracker, and never read the temporary file. JSON parse failures are not valid snapshots.

## UI behavior

**End and archive** is a single row-menu action; separate **End entry** and **Archive to Earlier days** remain available. Filing is optional, so unfiled entries archive without a forced warning.

A filed entry shows a small check mark. Its tooltip and accessible name include the original TimecardId and PostedAt. An archived entry without a posting ID shows neutral **Not filed** text. Expand **Earlier days** and select **Only archived, not filed** to show explicitly archived entries awaiting filing. Clearing the filter restores the full list without changing saved entries.
