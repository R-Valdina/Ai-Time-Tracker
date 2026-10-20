<#
.SYNOPSIS
Verifies installation/removal on an isolated Windows build runner, preserving a sample user-data marker.
.PARAMETER Action
Installs before packaged UI automation, or removes afterward and verifies retained user data.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Install', 'Uninstall')][string]$Action,
    [string]$InstallerPath
)
$ErrorActionPreference = 'Stop'
$installationDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/TimeTracker'
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TimeTracker'
$marker = Join-Path $dataDirectory 'installer-verification-sentinel.txt'
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/TimeTracker'
$shortcutFolders = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))

if ($Action -eq 'Install') {
    if (Test-Path $installationDirectory) { throw 'Installer verification needs a clean runner without an existing installation.' }
    if (-not $InstallerPath -or -not (Test-Path $InstallerPath)) { throw 'Provide the compiled installer path.' }
    New-Item $dataDirectory -ItemType Directory -Force | Out-Null
    'Retain user data across installation and removal.' | Set-Content $marker
    $process = Start-Process -FilePath $InstallerPath -ArgumentList '/S' -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode)." }
    foreach ($file in @('TimeTracker.exe', 'README.txt', 'INSTALL.txt', 'Uninstall.exe', 'coreclr.dll', 'hostfxr.dll', 'docs/images/time-tracker-light.png', 'docs/images/time-tracker-dark.png')) {
        if (-not (Test-Path (Join-Path $installationDirectory $file))) { throw "Missing installed file: $file" }
    }
    $configuration = Get-Content (Join-Path $installationDirectory 'TimeTracker.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $configuration.runtimeOptions.includedFrameworks) { throw 'The package must include its runtime rather than require a shared installation.' }
    if (-not (Test-Path $registryPath)) { throw 'The app was not registered in Windows Installed apps.' }
    foreach ($folder in $shortcutFolders) {
        if (-not (Test-Path (Join-Path $folder 'Time Tracker.lnk'))) { throw "Missing launcher shortcut in $folder." }
    }
    # Exercise replacement through the same setup path used by future updates.
    $replacement = Start-Process -FilePath $InstallerPath -ArgumentList '/S' -PassThru -Wait
    if ($replacement.ExitCode -ne 0 -or -not (Test-Path (Join-Path $installationDirectory 'TimeTracker.exe'))) {
        throw 'Reinstall/update verification failed.'
    }
    Write-Host "Verified self-contained installation, replacement, and shortcuts: $installationDirectory"
}
else {
    $uninstaller = Join-Path $installationDirectory 'Uninstall.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList '/S' -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Uninstall failed with exit code $($process.ExitCode)." }
    # NSIS removes its temporary uninstaller after the child process completes.
    $deadline = (Get-Date).AddSeconds(15)
    while ((Test-Path $installationDirectory) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (Test-Path $installationDirectory) { throw 'Uninstall left installed application files behind.' }
    if (Test-Path $registryPath) { throw 'Uninstall left its Windows Installed apps entry behind.' }
    foreach ($folder in $shortcutFolders) {
        if (Test-Path (Join-Path $folder 'Time Tracker.lnk')) { throw 'Uninstall left a launcher shortcut behind.' }
    }
    Write-Host 'Verified uninstallation removes application files, shortcuts, and registration.'
}
if (-not (Test-Path $marker) -or (Get-Content $marker -Raw).Trim() -ne 'Retain user data across installation and removal.') {
    throw 'Installation or removal changed the user-data marker.'
}
if ($Action -eq 'Uninstall') { Remove-Item $marker }
