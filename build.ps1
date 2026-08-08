[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\HyperTerm.UI\HyperTerm.UI.csproj'
$webTerminalPath = Join-Path $repositoryRoot 'src\HyperTerm.UI\WebTerminal'
$releaseRoot = Join-Path $repositoryRoot 'artifacts\releases'
$packageName = "HyperTerm-$Version-$Runtime"
$releaseStagingRoot = Join-Path $repositoryRoot "artifacts\staging\release-$PID"
$releasePublishPath = Join-Path $releaseStagingRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"
$archiveHashPath = "$archivePath.sha256"
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
$psmuxVersion = '3.3.7'
$psmuxLicensePath = Join-Path $repositoryRoot 'licenses\psmux-LICENSE.txt'
$psmuxPackages = @{
    'win-x64' = @{
        FileName = 'psmux-v3.3.7-windows-x64.zip'
        Sha256 = '60ff7b236f64184921cef3c1ff2611aa5a36fcc7ed8e2a58e968b8ded57f6028'
    }
    'win-arm64' = @{
        FileName = 'psmux-v3.3.7-windows-arm64.zip'
        Sha256 = '9404969b06f41acd1e7cbb56bbee074dc62389a650d8a7dbab71c8181e9b5efc'
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    Write-Host $Description
    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-PsmuxArchiveHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ExpectedHash
    )

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash) {
        throw "psmux archive SHA-256 mismatch. Expected $ExpectedHash, found $actualHash."
    }
}

function New-DeterministicZipArchive {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,

        [Parameter(Mandatory)]
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    $sourceRoot = [System.IO.Path]::GetFullPath($SourceDirectory)
    $fixedTimestamp = [System.DateTimeOffset]::new(
        1980,
        1,
        1,
        0,
        0,
        0,
        [System.TimeSpan]::Zero)
    $archiveStream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $files = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
            $files = $files | Sort-Object {
                [System.IO.Path]::GetRelativePath($sourceRoot, $_.FullName)
            }
            foreach ($file in $files) {
                $entryName = [System.IO.Path]::GetRelativePath(
                    $sourceRoot,
                    $file.FullName).Replace('\', '/')
                $entry = $zip.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $inputStream = $file.OpenRead()
                try {
                    $outputStream = $entry.Open()
                    try {
                        $inputStream.CopyTo($outputStream)
                    }
                    finally {
                        $outputStream.Dispose()
                    }
                }
                finally {
                    $inputStream.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK 10 or newer was not found.'
    }

    $dotnetPath = $dotnetCommand.Source
}

if (-not (Test-Path -LiteralPath $psmuxLicensePath)) {
    throw "psmux license was not found: $psmuxLicensePath"
}

$npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
if ($null -eq $npmCommand) {
    throw 'Node.js and npm were not found.'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseStagingRoot -Force | Out-Null

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path -LiteralPath $archiveHashPath) {
    Remove-Item -LiteralPath $archiveHashPath -Force
}

Invoke-CheckedCommand 'Installing web terminal dependencies...' {
    & $npmCommand.Source ci --prefix $webTerminalPath --no-audit --no-fund
}

Invoke-CheckedCommand 'Building the xterm.js bundle...' {
    & $npmCommand.Source run build --prefix $webTerminalPath
}

Invoke-CheckedCommand "Cleaning HyperTerm intermediates..." {
    & $dotnetPath clean $projectPath `
        --configuration Release `
        --nologo
}

Invoke-CheckedCommand "Publishing standard HyperTerm $Version for $Runtime..." {
    & $dotnetPath publish $projectPath `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $releasePublishPath `
        --nologo `
        -p:Version=$Version `
        -p:DebugType=None `
        -p:DebugSymbols=false
}

$releaseExecutablePath = Join-Path $releasePublishPath 'HyperTerm.exe'
if (-not (Test-Path -LiteralPath $releaseExecutablePath)) {
    throw "Standard executable was not found: $releaseExecutablePath"
}

$psmuxPackage = $psmuxPackages[$Runtime]
$psmuxCachePath = Join-Path $repositoryRoot "artifacts\cache\psmux\$psmuxVersion\$Runtime"
$psmuxArchivePath = Join-Path $psmuxCachePath $psmuxPackage.FileName
$psmuxDownloadUri = "https://github.com/psmux/psmux/releases/download/v$psmuxVersion/$($psmuxPackage.FileName)"
New-Item -ItemType Directory -Path $psmuxCachePath -Force | Out-Null

if (-not (Test-Path -LiteralPath $psmuxArchivePath)) {
    $partialArchivePath = "$psmuxArchivePath.download"
    if (Test-Path -LiteralPath $partialArchivePath) {
        Remove-Item -LiteralPath $partialArchivePath -Force
    }

    Write-Host "Downloading psmux $psmuxVersion for $Runtime..."
    Invoke-WebRequest -Uri $psmuxDownloadUri -OutFile $partialArchivePath
    try {
        Assert-PsmuxArchiveHash `
            -Path $partialArchivePath `
            -ExpectedHash $psmuxPackage.Sha256
        Move-Item -LiteralPath $partialArchivePath -Destination $psmuxArchivePath
    }
    catch {
        Remove-Item -LiteralPath $partialArchivePath -Force -ErrorAction SilentlyContinue
        throw
    }
}
else {
    Write-Host "Using cached psmux $psmuxVersion for $Runtime..."
    Assert-PsmuxArchiveHash `
        -Path $psmuxArchivePath `
        -ExpectedHash $psmuxPackage.Sha256
}

$psmuxExtractPath = Join-Path $releaseStagingRoot 'psmux'
Expand-Archive -LiteralPath $psmuxArchivePath -DestinationPath $psmuxExtractPath -Force
$psmuxExecutableCandidates = @(
    Get-ChildItem -LiteralPath $psmuxExtractPath -Filter 'psmux.exe' -File -Recurse
)
if ($psmuxExecutableCandidates.Count -ne 1) {
    throw "Expected one psmux.exe in $($psmuxPackage.FileName), found $($psmuxExecutableCandidates.Count)."
}

$bundledPsmuxDirectory = Join-Path $releasePublishPath 'tools\psmux'
$bundledLicenseDirectory = Join-Path $releasePublishPath 'licenses'
New-Item -ItemType Directory -Path $bundledPsmuxDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $bundledLicenseDirectory -Force | Out-Null
$bundledPsmuxPath = Join-Path $bundledPsmuxDirectory 'psmux.exe'
Copy-Item -LiteralPath $psmuxExecutableCandidates[0].FullName -Destination $bundledPsmuxPath
Copy-Item -LiteralPath $psmuxLicensePath `
    -Destination (Join-Path $bundledLicenseDirectory 'psmux-LICENSE.txt')

[xml]$centralPackages = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'Directory.Packages.props')
$packageVersions = [ordered]@{}
$sortedPackages = @($centralPackages.Project.ItemGroup.PackageVersion) | `
    Sort-Object Include
foreach ($package in $sortedPackages) {
    $packageVersions[$package.Include] = $package.Version
}
$webPackage = Get-Content -LiteralPath (
    Join-Path $webTerminalPath 'package.json') -Raw | ConvertFrom-Json
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'HyperTerm'
    version = $Version
    runtime = $Runtime
    targetFramework = 'net10.0'
    psmuxVersion = $psmuxVersion
    nugetPackages = $packageVersions
    npmDependencies = [ordered]@{
        '@xterm/addon-fit' = $webPackage.dependencies.'@xterm/addon-fit'
        '@xterm/addon-webgl' = $webPackage.dependencies.'@xterm/addon-webgl'
        '@xterm/xterm' = $webPackage.dependencies.'@xterm/xterm'
        esbuild = $webPackage.devDependencies.esbuild
    }
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $releasePublishPath 'HyperTerm.manifest.json') `
        -Encoding utf8NoBOM

$currentArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$canRunBundledPsmux =
    ($Runtime -eq 'win-x64' -and $currentArchitecture -eq [System.Runtime.InteropServices.Architecture]::X64) -or
    ($Runtime -eq 'win-arm64' -and $currentArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64)
if ($canRunBundledPsmux) {
    $bundledPsmuxVersion = (& $bundledPsmuxPath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $bundledPsmuxVersion -notmatch [regex]::Escape($psmuxVersion)) {
        throw "Bundled psmux version check failed: $bundledPsmuxVersion"
    }

    Write-Host "Bundled psmux verified: $bundledPsmuxVersion"
}
else {
    Write-Host "Skipping psmux execution check while cross-building $Runtime on $currentArchitecture."
}

Write-Host 'Creating complete ZIP package...'
New-DeterministicZipArchive `
    -SourceDirectory $releasePublishPath `
    -DestinationPath $archivePath

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "ZIP package was not created: $archivePath"
}

Remove-Item -LiteralPath $releaseStagingRoot -Recurse -Force

$archive = Get-Item -LiteralPath $archivePath
$archiveSizeMb = [Math]::Round($archive.Length / 1MB, 2)
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $archiveHashPath `
    -Value "$archiveHash  $($archive.Name)" `
    -Encoding ascii

Write-Host ''
Write-Host 'Build outputs created successfully:'
Write-Host "  Complete ZIP: $archivePath"
Write-Host "  SHA-256: $archiveHashPath"
Write-Host "  ZIP size: $archiveSizeMb MB"
