[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\HyperTerm.UI\HyperTerm.UI.csproj'
$webTerminalPath = Join-Path $repositoryRoot 'src\HyperTerm.UI\WebTerminal'
$releaseRoot = Join-Path $repositoryRoot 'artifacts\releases'
$packageName = "HyperTerm-$Version-$Runtime"
$releaseStagingRoot = Join-Path $repositoryRoot "artifacts\staging\release-$PID"
$releasePublishPath = Join-Path $releaseStagingRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"
$archiveHashPath = "$archivePath.sha256"
$installerBaseName = "$packageName-setup"
$installerPath = Join-Path $releaseRoot "$installerBaseName.exe"
$installerHashPath = "$installerPath.sha256"
$installerScriptPath = Join-Path $repositoryRoot 'installer\HyperTerm.iss'
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
$innoSetupVersion = '6.7.3'
$innoSetupInstallerHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$innoSetupCompilerHash = '0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7'
$innoSetupCachePath = Join-Path $repositoryRoot "artifacts\cache\inno-setup\$innoSetupVersion"
$innoSetupInstallerPath = Join-Path $innoSetupCachePath "innosetup-$innoSetupVersion.exe"
$innoSetupCompilerPath = Join-Path $innoSetupCachePath 'ISCC.exe'
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

function Assert-FileHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ExpectedHash
    )

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash) {
        throw "SHA-256 mismatch for $Path. Expected $ExpectedHash, found $actualHash."
    }
}

function Get-InnoSetupCompiler {
    if (Test-Path -LiteralPath $innoSetupCompilerPath) {
        Assert-FileHash `
            -Path $innoSetupCompilerPath `
            -ExpectedHash $innoSetupCompilerHash
        return $innoSetupCompilerPath
    }

    New-Item -ItemType Directory -Path $innoSetupCachePath -Force | Out-Null
    if (-not (Test-Path -LiteralPath $innoSetupInstallerPath)) {
        $partialInstallerPath = "$innoSetupInstallerPath.download"
        if (Test-Path -LiteralPath $partialInstallerPath) {
            Remove-Item -LiteralPath $partialInstallerPath -Force
        }

        Write-Host "Downloading Inno Setup $innoSetupVersion..."
        Invoke-WebRequest `
            -Uri "https://github.com/jrsoftware/issrc/releases/download/is-$($innoSetupVersion.Replace('.', '_'))/innosetup-$innoSetupVersion.exe" `
            -OutFile $partialInstallerPath
        try {
            Assert-FileHash `
                -Path $partialInstallerPath `
                -ExpectedHash $innoSetupInstallerHash
            Move-Item -LiteralPath $partialInstallerPath -Destination $innoSetupInstallerPath
        }
        catch {
            Remove-Item -LiteralPath $partialInstallerPath -Force -ErrorAction SilentlyContinue
            throw
        }
    }
    else {
        Assert-FileHash `
            -Path $innoSetupInstallerPath `
            -ExpectedHash $innoSetupInstallerHash
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $innoSetupInstallerPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=Pyrsys B\.V\.(,|$)') {
        throw 'The Inno Setup bootstrapper does not have a valid Pyrsys B.V. signature.'
    }

    Write-Host "Installing portable Inno Setup $innoSetupVersion..."
    $portableInstallArguments = @(
        '/PORTABLE=1',
        '/VERYSILENT',
        '/CURRENTUSER',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NOICONS',
        "/DIR=`"$innoSetupCachePath`"")
    $portableInstall = Start-Process `
        -FilePath $innoSetupInstallerPath `
        -ArgumentList $portableInstallArguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($portableInstall.ExitCode -ne 0) {
        throw "Portable Inno Setup installation failed with exit code $($portableInstall.ExitCode)."
    }

    if (-not (Test-Path -LiteralPath $innoSetupCompilerPath)) {
        throw "Inno Setup compiler was not created: $innoSetupCompilerPath"
    }

    Assert-FileHash `
        -Path $innoSetupCompilerPath `
        -ExpectedHash $innoSetupCompilerHash

    return $innoSetupCompilerPath
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

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Version must use the MAJOR.MINOR.PATCH format: $Version"
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK 10 or newer was not found.'
    }

    $dotnetPath = $dotnetCommand.Source
}

if (-not (Test-Path -LiteralPath $installerScriptPath)) {
    throw "Installer definition was not found: $installerScriptPath"
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
if ($Runtime -eq 'win-x64') {
    if (Test-Path -LiteralPath $installerPath) {
        Remove-Item -LiteralPath $installerPath -Force
    }
    if (Test-Path -LiteralPath $installerHashPath) {
        Remove-Item -LiteralPath $installerHashPath -Force
    }
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
$managedFiles = @(
    Get-ChildItem -LiteralPath $releasePublishPath -File -Recurse |
        ForEach-Object {
            [System.IO.Path]::GetRelativePath(
                $releasePublishPath,
                $_.FullName).Replace('\', '/')
        }
    'HyperTerm.manifest.json'
) | Sort-Object -Unique
$manifest = [ordered]@{
    schemaVersion = 2
    product = 'HyperTerm'
    version = $Version
    runtime = $Runtime
    targetFramework = 'net10.0'
    nugetPackages = $packageVersions
    npmDependencies = [ordered]@{
        '@xterm/addon-fit' = $webPackage.dependencies.'@xterm/addon-fit'
        '@xterm/addon-webgl' = $webPackage.dependencies.'@xterm/addon-webgl'
        '@xterm/xterm' = $webPackage.dependencies.'@xterm/xterm'
        esbuild = $webPackage.devDependencies.esbuild
    }
    files = $managedFiles
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $releasePublishPath 'HyperTerm.manifest.json') `
        -Encoding utf8NoBOM

Write-Host 'Creating complete ZIP package...'
New-DeterministicZipArchive `
    -SourceDirectory $releasePublishPath `
    -DestinationPath $archivePath

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "ZIP package was not created: $archivePath"
}

if ($Runtime -eq 'win-x64') {
    $innoSetupCompiler = Get-InnoSetupCompiler
    Invoke-CheckedCommand 'Creating per-user installer...' {
        & $innoSetupCompiler `
            "/DAppVersion=$Version" `
            "/DSourceDirectory=$releasePublishPath" `
            "/DOutputDirectory=$releaseRoot" `
            "/DOutputBaseFilename=$installerBaseName" `
            "/DSetupIconPath=$(Join-Path $repositoryRoot 'assets\hyperterm_minimal.ico')" `
            $installerScriptPath
    }

    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Installer was not created: $installerPath"
    }
}

Remove-Item -LiteralPath $releaseStagingRoot -Recurse -Force

$archive = Get-Item -LiteralPath $archivePath
$archiveSizeMb = [Math]::Round($archive.Length / 1MB, 2)
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $archiveHashPath `
    -Value "$archiveHash  $($archive.Name)" `
    -Encoding ascii

if ($Runtime -eq 'win-x64') {
    $installer = Get-Item -LiteralPath $installerPath
    $installerSizeMb = [Math]::Round($installer.Length / 1MB, 2)
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $installerHashPath `
        -Value "$installerHash  $($installer.Name)" `
        -Encoding ascii
}

Write-Host ''
Write-Host 'Build outputs created successfully:'
Write-Host "  Complete ZIP: $archivePath"
Write-Host "  SHA-256: $archiveHashPath"
Write-Host "  ZIP size: $archiveSizeMb MB"
if ($Runtime -eq 'win-x64') {
    Write-Host "  Installer: $installerPath"
    Write-Host "  SHA-256: $installerHashPath"
    Write-Host "  Installer size: $installerSizeMb MB"
}
