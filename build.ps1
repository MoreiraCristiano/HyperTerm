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
$publishPath = Join-Path $releaseRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"
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

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Invoke-CheckedCommand 'Installing web terminal dependencies...' {
    & $npmCommand.Source ci --prefix $webTerminalPath --no-audit --no-fund
}

Invoke-CheckedCommand 'Building the xterm.js bundle...' {
    & $npmCommand.Source run build --prefix $webTerminalPath
}

Invoke-CheckedCommand "Cleaning HyperTerm $Runtime intermediates..." {
    & $dotnetPath clean $projectPath `
        --configuration Release `
        --runtime $Runtime `
        --nologo
}

Invoke-CheckedCommand "Publishing HyperTerm $Version for $Runtime..." {
    & $dotnetPath publish $projectPath `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $publishPath `
        --nologo `
        -p:Version=$Version `
        -p:DebugType=None `
        -p:DebugSymbols=false
}

$executablePath = Join-Path $publishPath 'HyperTerm.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Published executable was not found: $executablePath"
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
[HyperTermShellChangeNotifier]::RefreshIcon($executablePath)

Write-Host 'Creating portable ZIP package...'
Compress-Archive -LiteralPath $publishPath -DestinationPath $archivePath -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "ZIP package was not created: $archivePath"
}

$archive = Get-Item -LiteralPath $archivePath
$archiveSizeMb = [Math]::Round($archive.Length / 1MB, 2)

Write-Host ''
Write-Host 'Portable package created successfully:'
Write-Host "  $archivePath"
Write-Host "  Size: $archiveSizeMb MB"
