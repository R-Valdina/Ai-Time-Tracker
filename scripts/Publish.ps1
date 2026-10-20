<#
.SYNOPSIS
Builds a self-contained offline ZIP and optionally a per-user Windows installer with guides and screenshots.
.PARAMETER Runtime
Selects the Windows processor architecture. The tested setup executable currently targets win-x64.
.PARAMETER CreateInstaller
Compiles setup with the free, open-source NSIS compiler installed on the build machine.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$CreateInstaller
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$publishDirectory = Join-Path $repositoryRoot "artifacts/TimeTracker-$Runtime"
[xml]$buildProperties = Get-Content (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
$version = [string]$buildProperties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Set a three-part release Version in Directory.Build.props.' }
Push-Location $repositoryRoot
try {
    dotnet test tests/TimeTracker.Core.Tests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests must pass before packaging.' }
    # A fresh output folder prevents old runtime files from leaking into a new release.
    if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }
    dotnet publish src/TimeTracker.Desktop --configuration Release --runtime $Runtime --self-contained true --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item scripts/Install-TimeTracker.ps1 $publishDirectory
    Copy-Item LICENSE $publishDirectory
    Copy-Item README.md $publishDirectory
    Copy-Item AGENTS.md $publishDirectory
    Copy-Item README.md (Join-Path $publishDirectory 'README.txt')
    Copy-Item docs/installation.md (Join-Path $publishDirectory 'INSTALL.txt')
    New-Item (Join-Path $publishDirectory 'docs') -ItemType Directory -Force | Out-Null
    Copy-Item docs/*.md (Join-Path $publishDirectory 'docs')
    Copy-Item docs/third-party-notices.md (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md')
    if (Test-Path docs/images) { Copy-Item docs/images (Join-Path $publishDirectory 'docs/images') -Recurse }
    $archive = Join-Path $repositoryRoot "artifacts/TimeTracker-$version-$Runtime.zip"
    Compress-Archive -Path "$publishDirectory/*" -DestinationPath $archive -Force
    Write-Host "Created $archive."

    if ($CreateInstaller) {
        if ($Runtime -ne 'win-x64' -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            throw 'The setup compiler currently requires Windows and a win-x64 payload.'
        }
        $compiler = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe'
        if (-not (Test-Path $compiler)) { throw 'Install the free NSIS compiler before using -CreateInstaller.' }
        $installer = Join-Path $repositoryRoot "artifacts/TimeTracker-Setup-$version-$Runtime.exe"
        # Derive uninstall operations from the exact payload; remove directories only when empty.
        $manifest = Join-Path $repositoryRoot 'artifacts/uninstall-files.nsh'
        $lines = foreach ($file in Get-ChildItem $publishDirectory -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($publishDirectory, $file.FullName).Replace('/', '\').Replace('$', '$$')
            'Delete "$INSTDIR\{0}"' -f $relative
        }
        $lines += foreach ($directory in Get-ChildItem $publishDirectory -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
            $relative = [IO.Path]::GetRelativePath($publishDirectory, $directory.FullName).Replace('/', '\').Replace('$', '$$')
            'RMDir "$INSTDIR\{0}"' -f $relative
        }
        $lines | Set-Content $manifest -Encoding utf8
        & $compiler /V3 "/DAPP_VERSION=$version" "/DSOURCE_DIRECTORY=$publishDirectory" "/DOUTPUT_FILE=$installer" "/DUNINSTALL_MANIFEST=$manifest" packaging/TimeTracker.nsi
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        Write-Host "Created $installer."
    }
    Copy-Item README.md artifacts/README.txt
    Copy-Item docs/installation.md artifacts/INSTALL.txt
    $checksums = Get-ChildItem artifacts -File | Where-Object { $_.Name -match "^TimeTracker.*$([regex]::Escape($version)).*\.(exe|zip)$" } |
        Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    $checksums | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
}
finally { Pop-Location }
