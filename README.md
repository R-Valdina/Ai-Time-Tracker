# Time Tracker

A small, offline Windows desktop app for tracking time across development cards. Built with C#/.NET 10 and WPF. No accounts, telemetry, remote services, database server, or required subscription.

## Use the app

- Add a card reference and choose a row border color: blue for features, red for bugs, or another accent.
- Use a single Start/Stop button to accumulate time. Multiple cards may run simultaneously.
- End an entry to keep its final total visible. Copy hours/minutes or total whole minutes from its menu; partial minutes are not rounded up.
- Entries stay until manually deleted, with confirmation. Starting an older card creates or resumes today's unfinished entry, keeping the previous total.
- Windows lock, sleep, session disconnection, and normal exit pause active timers. Unlock/wake does not restart them.
- At midnight, timers stop at the local day boundary and are highlighted for review. Acknowledge the warning from the row menu.
- Choose Light, Dark, or Follow Windows from Settings. The window grows with rows until it reaches the screen limit; manual resizing takes precedence. Normal bounds and maximized state are remembered on exit.

The design reference is [the approved mockup](docs/design/card-timer-mockup.png). The application uses the agreed name **Time Tracker** and omits the mockup's optional work-type subtitle.

## Develop on Windows

1. Install **Visual Studio Community 2026** with **.NET desktop development**, including the .NET 10 SDK. Individual personal use is free under Microsoft's terms; the IDE itself is not open source.
2. Clone this public repository, then open **TimeTracker.slnx** in Visual Studio.
3. Set **TimeTracker.Desktop** as the startup project and press **F5**.
4. Use **Test → Test Explorer** for unit and automation tests. Desktop automation needs an unlocked interactive Windows session; don't operate the tested window while the suite runs.

Command-line alternative with the .NET 10 SDK:

```powershell
dotnet build TimeTracker.slnx --configuration Release
dotnet run --project src/TimeTracker.Desktop
./scripts/Test.ps1 -IncludeUi
```

Developers need internet access to download the SDK and NuGet packages initially. The installed application runs entirely offline. VS Code can edit/build the solution too, but Visual Studio supplies the WPF designer.

## Local files you can find

Choose **Settings → Open data folder** or paste `%LOCALAPPDATA%\TimeTracker` into Windows File Explorer:

```text
tracker-data.json          Card references, dates, colors, totals, and ended/review flags
tracker-data.backup.json   Previous successful tracker save
preferences.json          Theme and window placement
preferences.backup.json   Previous successful preferences save
time-tracker.lock         Prevents concurrent writers; contains no tracker data
```

Data saves after actions and every five seconds while the app is open. A temporary file is flushed before atomic replacement. A corrupt primary recovers from its backup with a warning; two unreadable files stop startup rather than erase your information. Back up these files with the app closed. They contain plain text, not encrypted data, and are not intended for cloud-sync folders or concurrent users.

On unexpected termination, timers reopen paused at the last successful checkpoint. Up to five seconds can be lost under normal conditions; disk failures or a forced termination before a checkpoint can lose more. A backup may be one checkpoint older. Normal exit and supported Windows lifecycle events save immediately.

## Tests and releases

- [Testing commands, automated coverage, and Windows validation checklist](docs/testing.md)
- [Architecture and code walkthrough](docs/architecture.md)
- [Packaging, offline installation, and shortcuts](docs/installation.md)
- [Dependencies and licensing](docs/third-party-notices.md)

GitHub Actions is configured to build and test pull requests. These workflows are optional; every check can run locally without a paid testing service. Hosted runner availability, licensing, and billing are controlled by GitHub, not by this app. Required branch checks must be enabled separately in repository settings.

## Current validation status

Linux can run the core tests and cross-compile WPF and FlaUI projects, but cannot run the Windows application. Successful cross-compilation does **not** verify appearance, lock/sleep integration, UI automation, installer behavior, or a clean Windows installation. Complete the Windows checklist before calling a release validated.

## License

MIT; see [LICENSE](LICENSE). Contributions should follow [AGENTS.md](AGENTS.md): descriptive names, XML hover documentation, shared templates, and regression tests for behavior changes.
