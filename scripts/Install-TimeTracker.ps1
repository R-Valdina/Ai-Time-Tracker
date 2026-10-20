<#
.SYNOPSIS
Installs a packaged Time Tracker for the current Windows user and creates launcher shortcuts.
.DESCRIPTION
Run from an extracted release beside TimeTracker.exe. Does not require administrator access,
download anything, modify tracker data, or change PowerShell execution policy.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Time Tracker requires Windows.' }
$sourceExecutable = Join-Path $PSScriptRoot 'TimeTracker.exe'
if (-not (Test-Path $sourceExecutable)) { throw 'Extract the release ZIP first, then run the installer beside TimeTracker.exe.' }
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/TimeTracker'
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -eq [IO.Path]::GetFullPath($destination).TrimEnd('\')) {
    throw 'Run this installer from the extracted release folder, not the installed application folder.'
}
$runningTracker = Get-Process -Name TimeTracker -ErrorAction SilentlyContinue
if ($runningTracker) { throw 'Close Time Tracker before installing or updating.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '*') -Destination $destination -Recurse -Force
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($shortcutFolder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        $shortcut = $shell.CreateShortcut((Join-Path $shortcutFolder 'Time Tracker.lnk'))
        $shortcut.TargetPath = Join-Path $destination 'TimeTracker.exe'
        $shortcut.WorkingDirectory = $destination
        $shortcut.Description = 'Track time on development cards, entirely offline.'
        $shortcut.IconLocation = "$destination\TimeTracker.exe,0"
        $shortcut.Save()
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
    }
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
Write-Host "Installed to $destination. Open Time Tracker from the desktop or Start menu."
Write-Host 'Your data is stored separately in %LOCALAPPDATA%\TimeTracker and was not changed.'
