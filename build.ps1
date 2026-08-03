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
$portableRoot = Join-Path $repositoryRoot 'artifacts\portable'
$portablePublishPath = Join-Path $portableRoot 'win-x64'
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'

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

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK 9 or newer was not found.'
    }

    $dotnetPath = $dotnetCommand.Source
}

$npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
if ($null -eq $npmCommand) {
    throw 'Node.js and npm were not found.'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseStagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

if (Test-Path -LiteralPath $portablePublishPath) {
    Remove-Item -LiteralPath $portablePublishPath -Recurse -Force
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

Write-Host 'Creating standard portable ZIP package...'
Compress-Archive -LiteralPath $releasePublishPath -DestinationPath $archivePath -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "ZIP package was not created: $archivePath"
}

Remove-Item -LiteralPath $releaseStagingRoot -Recurse -Force

Invoke-CheckedCommand "Publishing single-file HyperTerm $Version for win-x64..." {
    & $dotnetPath publish $projectPath `
        --configuration Release `
        --output $portablePublishPath `
        --nologo `
        -p:Version=$Version `
        -p:PublishProfile=win-x64-portable
}

$portableExecutablePath = Join-Path $portablePublishPath 'HyperTerm.exe'
if (-not (Test-Path -LiteralPath $portableExecutablePath)) {
    throw "Portable executable was not found: $portableExecutablePath"
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HyperTermShellChangeNotifier
{
    private const uint UpdateItem = 0x00002000;
    private const uint PathW = 0x0005;
    private const uint Flush = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string item1,
        IntPtr item2);

    public static void RefreshIcon(string path) =>
        SHChangeNotify(UpdateItem, PathW | Flush, path, IntPtr.Zero);
}
'@
[HyperTermShellChangeNotifier]::RefreshIcon($releaseExecutablePath)
[HyperTermShellChangeNotifier]::RefreshIcon($portableExecutablePath)

Get-ChildItem -LiteralPath $portablePublishPath -Filter '*.pdb' -File -Recurse |
    Remove-Item -Force

$publishedFiles = @(Get-ChildItem -LiteralPath $portablePublishPath -File -Recurse)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'HyperTerm.exe') {
    $publishedNames = $publishedFiles.Name -join ', '
    throw "Portable output must contain only HyperTerm.exe. Found: $publishedNames"
}

$archive = Get-Item -LiteralPath $archivePath
$archiveSizeMb = [Math]::Round($archive.Length / 1MB, 2)
$portableExecutable = Get-Item -LiteralPath $portableExecutablePath
$portableSizeMb = [Math]::Round($portableExecutable.Length / 1MB, 2)

Write-Host ''
Write-Host 'Build outputs created successfully:'
Write-Host "  Standard ZIP: $archivePath"
Write-Host "  ZIP size: $archiveSizeMb MB"
Write-Host "  Single-file executable: $portableExecutablePath"
Write-Host "  Executable size: $portableSizeMb MB"
