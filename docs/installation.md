# Install Time Tracker

Time Tracker is an offline Windows desktop app. The downloadable release includes its .NET runtime, application files, README, and this installation guide. You do not need Git, a repository clone, Visual Studio, an SDK, a separate .NET installation, or an account.

## Install the Windows download

1. Go to https://github.com/R-Valdina/Ai-Time-Tracker/releases/latest.
2. Under **Assets**, download `TimeTracker-Setup-0.1.0-win-x64.exe`. This release targets Windows 11 on 64-bit Intel/AMD PCs.
3. Close Time Tracker if an older copy is running.
4. Double-click the downloaded installer and follow its wizard. Administrator access is not required.
5. Keep the default location, or choose a dedicated application folder. The default is `%LOCALAPPDATA%\Programs\TimeTracker`.
6. Finish setup and open Time Tracker using its desktop or Start menu shortcut.
7. Select **+ New tracker**, enter a card reference and optional description, choose a color, and save. Select **Start** to begin timing.

Installation and use need no internet connection after downloading. The first installer is unsigned; Windows may show an unknown-publisher or reputation warning. Follow your organization's or device's security policy rather than bypassing its restrictions.

The release page provides `README.txt` and `INSTALL.txt` to read before installation, plus `SHA256SUMS.txt` for verifying downloads. Both guides are also installed beside `TimeTracker.exe`. The setup wizard has an optional **Read the usage and installation guide** checkbox at the end.

## Portable use

1. Download `TimeTracker-0.1.0-win-x64.zip` from the same release.
2. Extract the **entire** ZIP into a folder. Do not run from inside the compressed folder.
3. Open `TimeTracker.exe`. Keep all extracted app/runtime files together.
4. Optionally create a Windows shortcut to that executable.

Portable use changes where the application files live, not where tracker data is saved. The older `Install-TimeTracker.ps1` helper remains available in the ZIP for people who prefer a script; the setup executable is the recommended installation route. Script policies may prevent running that helper. Setup does not require it or change execution policy.

## Find and back up your data

In the app, select **Settings → Open data folder**, or paste `%LOCALAPPDATA%\TimeTracker` into File Explorer.

`tracker-data.json` contains your entries and totals. `preferences.json` contains your theme and window placement. Each has a `.backup.json` file. Close the app before copying this folder as a backup. These readable files are not encrypted.

## Update

1. Close Time Tracker and back up `%LOCALAPPDATA%\TimeTracker`.
2. Download the newer setup executable from Releases.
3. Run it and use your existing application folder.
4. Open Time Tracker and confirm your retained entries and totals.

The installer replaces application files and refreshes shortcuts; it leaves your separate tracker-data folder untouched. Portable users should extract a new release into a fresh folder and update their shortcut.

## Uninstall

1. Close Time Tracker.
2. Open **Windows Settings → Apps → Installed apps → Time Tracker → Uninstall**, or run `Uninstall.exe` in its application folder.
3. Follow the removal prompt. App files, shortcuts, and registration are removed; your tracker-data folder stays.
4. Only if you also want to erase your saved cards and preferences, manually delete `%LOCALAPPDATA%\TimeTracker` after backing it up.

For portable or script-installed copies, close the app and delete its application folder and any shortcuts. Do not delete the separate data folder unless you intend to remove your saved information.

## Verify a download

From PowerShell in the Downloads folder, replace the filename if the version differs:

```powershell
Get-FileHash .\TimeTracker-Setup-0.1.0-win-x64.exe -Algorithm SHA256
```

Compare the result with the same filename in `SHA256SUMS.txt` on the release page. Matching hashes check file integrity, not publisher identity or a digital signature.

## Build a release from source

This section is for maintainers; end users use the downloads above.

With the .NET 10 SDK and PowerShell, from the repository root:

```powershell
./scripts/Test.ps1 -IncludeUi
./scripts/Publish.ps1
```

The publish script runs core tests and creates a self-contained folder and versioned ZIP under `artifacts`. For a Windows setup executable, install the free, open-source **NSIS 3.11** compiler in its default location, then run:

```powershell
./scripts/Publish.ps1 -CreateInstaller
```

The setup compiler uses the shared version in `Directory.Build.props`. Runtime notices, the README, installation guide, and screenshots are copied into the payload. NSIS is a build tool and is not required on an end user's computer. Code signing is not configured.

The **Package and release** workflow runs source tests, captures actual light/dark WPF screenshots into `docs/images`, builds the package, silently installs it on an isolated Windows runner, checks shortcuts/runtime/guides, runs UI tests against the installed executable, and verifies uninstall retains sample user data. Never run `Test-Installer.ps1` on a personal installation; it expects a disposable build runner.

A tag named `v` plus the project version (for example `v0.1.0`) publishes the tested installer, portable ZIP, guides, and hashes to GitHub Releases. Branch builds retain the same files as CI artifacts and update repository screenshots after verification. Publishing requires the workflow's repository write permission.

Complete the manual checks in `docs/testing.md` for Windows lock/sleep, actual Narrator speech, scaling, and a clean physical PC. Hosted automation does not establish those outcomes or simulate a machine without an installed SDK. The package's included runtime and execution against installed files are verified separately.
