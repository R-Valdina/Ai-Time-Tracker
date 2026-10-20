# Install Time Tracker on Windows

Time Tracker targets Windows desktop with .NET 10. Use a supported Windows version (Windows 11 recommended). Verify the release on the target machine before distributing it. No accounts or administrator privileges are required by the app.

## From a packaged release

1. Download the matching `TimeTracker-win-x64.zip` (most Intel/AMD PCs) or `TimeTracker-win-arm64.zip` (ARM PCs).
2. Extract the entire ZIP. Do not launch from inside the compressed folder.
3. Run `TimeTracker.exe` directly for portable use, or review and run `Install-TimeTracker.ps1` from that same extracted folder to copy it to `%LOCALAPPDATA%\Programs\TimeTracker` and add desktop/Start menu shortcuts.

The package includes the .NET runtime and all required application files. It does not download resources when opened. Keep the extracted application files together. A portable app still saves data in the current user's `%LOCALAPPDATA%\TimeTracker` directory; Settings → Open data folder opens it.

The initial package is unsigned. Windows may display reputation or downloaded-script warnings. Verify the source/release before running it and follow your machine's security policy. The installer does not change execution policy; if scripts are blocked, use the executable directly and create a shortcut manually with Windows' Create shortcut command.

To update, close the app and install/extract the new package. Keep a copy of your data first. The installer leaves tracker data separate and untouched. To remove the app, close it, delete `%LOCALAPPDATA%\Programs\TimeTracker`, and delete its desktop/Start menu shortcuts. Your data stays until you explicitly delete `%LOCALAPPDATA%\TimeTracker`.

## Build your own package

With the .NET 10 SDK and PowerShell, from the repository root:

```powershell
./scripts/Publish.ps1
# For ARM Windows:
./scripts/Publish.ps1 -Runtime win-arm64
```

This runs core tests and creates a self-contained folder and ZIP under `artifacts`. It does not publish a GitHub release or claim Windows UI validation. Follow `docs/testing.md` and run UI automation against the package before releasing it:

```powershell
$env:TIME_TRACKER_EXECUTABLE = (Resolve-Path ./artifacts/TimeTracker-win-x64/TimeTracker.exe).Path
./scripts/Test.ps1 -IncludeUi
```

Visual Studio's WPF designer is optional for running the app. Developer restore/build needs package downloads; end users do not need the SDK, Visual Studio, NuGet, or internet access.
