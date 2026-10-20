<#
.SYNOPSIS
Creates a self-contained, offline Windows package after building and running unit tests.
.PARAMETER Runtime
Selects 64-bit Intel/AMD or ARM Windows. Packaging does not claim Windows UI validation.
#>
[CmdletBinding()]
param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$publishDirectory = Join-Path $repositoryRoot "artifacts/TimeTracker-$Runtime"
Push-Location $repositoryRoot
try {
    dotnet test tests/TimeTracker.Core.Tests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests must pass before packaging.' }
    dotnet publish src/TimeTracker.Desktop --configuration Release --runtime $Runtime --self-contained true --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item scripts/Install-TimeTracker.ps1 $publishDirectory
    Copy-Item LICENSE $publishDirectory
    Copy-Item docs/installation.md (Join-Path $publishDirectory 'INSTALL.md')
    Copy-Item docs/third-party-notices.md (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md')
    $archive = Join-Path $repositoryRoot "artifacts/TimeTracker-$Runtime.zip"
    Compress-Archive -Path "$publishDirectory/*" -DestinationPath $archive -Force
    Write-Host "Created $archive. Verify the package on Windows before publishing a release."
}
finally { Pop-Location }
