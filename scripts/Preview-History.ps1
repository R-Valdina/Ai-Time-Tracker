<#
.SYNOPSIS
Opens an isolated sample tracker window to preview earlier days without changing personal data.
.DESCRIPTION
Requires Windows and the .NET 10 SDK. Each launch creates a new temporary folder, prints its
location, and leaves the sample files there for inspection after the window closes.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$previewDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "TimeTracker-History-Preview-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $previewDirectory | Out-Null
$yesterday = (Get-Date).Date.AddDays(-1).ToString('yyyy-MM-dd')
$entries = @(
    @{ Id = [guid]::NewGuid().ToString(); Description = 'Review acceptance criteria and confirm the release checklist'; Label = 'FEATURE-1428'; Color = '#2563EB'; Date = $yesterday; Elapsed = '01:25:00'; IsEnded = $true; NeedsReview = $false; IsArchived = $false },
    @{ Id = [guid]::NewGuid().ToString(); Description = 'Investigate the intermittent sign-in failure'; Label = 'BUG-903'; Color = '#DC2626'; Date = $yesterday; Elapsed = '00:42:00'; IsEnded = $false; NeedsReview = $true; IsArchived = $false }
)
@{ Version = 1; Entries = $entries } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $previewDirectory 'tracker-data.json') -Encoding utf8
Write-Host "Sample data only: $previewDirectory"
dotnet run --project (Join-Path $repositoryRoot 'src/TimeTracker.Desktop') --configuration Release -- --data-directory $previewDirectory
if ($LASTEXITCODE -ne 0) { throw 'History preview failed to start.' }
