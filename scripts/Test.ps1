<#
.SYNOPSIS
Builds Time Tracker and runs its regression suites without requiring a paid test service.
.PARAMETER IncludeUi
Runs FlaUI against a real Windows desktop. Keep the session unlocked while it runs.
#>
[CmdletBinding()]
param([switch]$IncludeUi)

$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
Reports executed test counts and failure details in the GitHub run summary without hiding failed tests.
#>
function Write-TestSummary {
    param([string]$ResultPath, [string]$SuiteName)
    if (-not (Test-Path $ResultPath)) { throw "Missing results for $SuiteName." }
    [xml]$results = Get-Content -Path $ResultPath -Raw
    $counts = $results.TestRun.ResultSummary.Counters
    if ([int]$counts.total -lt 1) { throw "No tests executed for $SuiteName." }
    if ($env:GITHUB_STEP_SUMMARY) {
        $lines = @("### $SuiteName", '', "Total: $($counts.total) | Passed: $($counts.passed) | Failed: $($counts.failed)", '')
        foreach ($test in $results.TestRun.Results.UnitTestResult) {
            if ($test.outcome -eq 'Failed') {
                $lines += @("#### $($test.testName)", '', '~~~text', [string]$test.Output.ErrorInfo.Message,
                    [string]$test.Output.ErrorInfo.StackTrace, '~~~', '')
                # Annotations expose actionable failures alongside the job status, preserving the full TRX artifact.
                $annotation = "$($test.testName)`n$($test.Output.ErrorInfo.Message)`n$($test.Output.ErrorInfo.StackTrace)"
                $annotation = $annotation.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
                Write-Host "::error::$annotation"
            }
        }
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($lines -join [Environment]::NewLine)
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repositoryRoot
try {
    dotnet build TimeTracker.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test tests/TimeTracker.Core.Tests --configuration Release --no-build --logger 'trx;LogFileName=core-tests.trx'
    $coreExitCode = $LASTEXITCODE
    Write-TestSummary 'tests/TimeTracker.Core.Tests/TestResults/core-tests.trx' 'Core unit tests'
    if ($coreExitCode -ne 0) { throw 'Unit tests failed.' }
    if ($IncludeUi) {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'UI automation requires Windows.' }
        dotnet test tests/TimeTracker.Ui.Tests --configuration Release --no-build --logger 'trx;LogFileName=ui-tests.trx'
        $uiExitCode = $LASTEXITCODE
        Write-TestSummary 'tests/TimeTracker.Ui.Tests/TestResults/ui-tests.trx' 'Windows UI and presentation tests'
        if ($uiExitCode -ne 0) { throw 'Windows UI automation failed.' }
    }
}
finally { Pop-Location }
