# Testing standards

Behavior changes require relevant tests; bug fixes require a regression that fails without the fix. Do not disable assertions, silently skip required tests, or report a zero-test run as validation. A passing cross-build is not a Windows runtime test.

## Local commands

From the repository root, run:

```powershell
# Build every project and run the core suite.
./scripts/Test.ps1

# Also run FlaUI against a real app window on Windows.
./scripts/Test.ps1 -IncludeUi
```

Or use `dotnet test tests/TimeTracker.Core.Tests -c Release` directly. `dotnet test TimeTracker.slnx` includes Windows-only UI tests and therefore requires Windows. Test Explorer builds both the app and tests; match the chosen Debug/Release configuration.

FlaUI tests use unique temporary data folders via `--data-directory`; they never touch your normal files. They use automation IDs and observable-state waits rather than screen coordinates or fixed sleeps. Tests run sequentially because they share the desktop. Set `TIME_TRACKER_EXECUTABLE` to test an explicit packaged executable; otherwise the suite discovers the matching build output. A missing executable is a test failure, not a skip.

The core suite covers start/pause/resume, overlapping timers, repeated sampling, end/delete, editing, midnight truncation, local dates, new daily entries, backwards clock adjustments, recovery to paused state, formatting, invalid input, atomic persistence, backup fallback, damaged data, failed writes, and preferences.

The UI suite covers create/edit, delete cancellation/confirmation, start/stop/end, reopened totals, simultaneous timers, close-to-pause, theme persistence, window width restoration, forced termination/checkpoint recovery, and visible midnight warnings/history. It must be executed on Windows before those workflows are considered verified. UI tests require access to an unlocked desktop and UI Automation; do not lock the machine during the run.

WPF presentation regressions also inspect the actual main/editor/confirmation window backgrounds while switching themes, neutral raised creation buttons, green Start/red Stop states, and rounded menu surfaces without the stock icon gutter. Finalized totals must remain visible on the row after reopening. These checks catch styling errors that a saved preference alone cannot detect.

Mouse/keyboard regressions check that completed mouse actions release focus, keyboard activation retains its place, focused buttons keep their normal border, and timer/menu accessibility names identify the card. Shadow strength is checked in both themes. These checks validate UI Automation exposure; a manual Narrator pass is still required to assess the actual spoken experience.

## Automation in GitHub

`.github/workflows/build-and-test.yml` runs core tests on Linux and builds/tests the app on a Windows runner for pull requests, pushes to main, and manual runs. Result files are uploaded even when tests fail. If the hosted runner cannot provide a usable interactive desktop, investigate that failure and run the Windows suite locally; do not convert it into a silently passing check. Local test execution has no usage fee. Hosted CI is optional and subject to provider terms.

The Windows script adds executed test counts and failure details to the GitHub run summary. A missing result file or zero-test run fails the check. The underlying test failure still fails the job after its summary is written.

## Required manual Windows release checks

These exercise real OS behavior that should not be simulated by a passing unit test:

- Start a timer, lock Windows, wait, then unlock. The timer must be paused; locked time must not accrue. Start again, sleep Windows, wake, and verify the same behavior.
- Close normally with active timers and reopen. All entries must remain with paused totals. Kill the process separately and confirm recovery to the last checkpoint.
- Run through midnight (or use a temporary, isolated test environment clock). Confirm exact day-boundary stop, visible warning, acknowledgement, and a fresh next-day entry. Avoid changing your work machine's clock.
- Check light/dark/system themes, Windows preference changes, high contrast, keyboard-only operation, long labels, 125–200% display scaling, row growth, scrolling, maximization, restored placement, and monitor changes.
- Turn on Windows Narrator with Ctrl+Windows+Enter and navigate with Tab/Shift+Tab. Confirm card-specific Start/Stop and row-menu names, the labeled reference input, menu navigation, dialog focus, and readable ended totals. Disable Narrator with the same shortcut when finished. Speech requires a screen reader; the app does not speak by itself.
- Try saving into an unwritable isolated folder. Confirm visible errors and the warning before discarding unsaved time on close.
- Verify copy formats against known totals, including seconds below a whole minute; confirm no rounding up.
- Test the self-contained release on Windows without the .NET SDK and without internet access; verify shortcuts, update behavior, and retained data.

Record Windows version, build commit, executed test counts, manual outcomes, and any failures before release. The initial Linux validation cannot satisfy this checklist.

## Preview Earlier days without changing your data

On Windows, open a PowerShell terminal in the repository root and run:

```powershell
./scripts/Preview-History.ps1
```

This builds and opens a separate sample window containing two yesterday entries: one ended total and one midnight review warning. Earlier days opens automatically because of the warning. The script prints the unique temporary sample folder; your normal `%LOCALAPPDATA%\TimeTracker` files remain untouched. Close the sample window when finished. You may delete the printed temporary folder afterward.

To try archiving with your own entries, end a tracker, open its row menu, and select **Archive to Earlier days**. The section opens automatically. Archiving retains the original date and total, persists after reopening, and does not delete the tracker. Today's archived entries remain ended; on a later day, **Start today** creates a fresh daily entry. An optional description of up to 160 characters appears below the total, wraps across two reserved lines, and remains with archived entries. Add or change it through the shared tracker editor. Long notes have a tooltip with the full text.

UI regressions also verify friendly collection names exposed to Narrator and the archive-to-history workflow. Core coverage checks that archiving requires an ended entry and preserves its data across recovery.

Narrator regression coverage inspects the actual `DataItem` peers WPF generates for tracker objects, including renaming and moving an entry into history. Their names must be `Tracker <card reference>`, never a .NET namespace/type name. The enclosing scroll area also has an explicit `Trackers` name. Check these items with Narrator hover/navigation as well as the Tab-accessible buttons.
