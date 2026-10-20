<#
.SYNOPSIS
Builds Time Tracker and runs its regression suites without requiring a paid test service.
.PARAMETER IncludeUi
Runs FlaUI against a real Windows desktop. Keep the session unlocked while it runs.
#>
[CmdletBinding()]
param([switch]$IncludeUi)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repositoryRoot
try {
    dotnet build TimeTracker.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test tests/TimeTracker.Core.Tests --configuration Release --no-build --logger 'trx;LogFileName=core-tests.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    if ($IncludeUi) {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'UI automation requires Windows.' }
        dotnet test tests/TimeTracker.Ui.Tests --configuration Release --no-build --logger 'trx;LogFileName=ui-tests.trx'
        if ($LASTEXITCODE -ne 0) { throw 'Windows UI automation failed.' }
    }
}
finally { Pop-Location }
