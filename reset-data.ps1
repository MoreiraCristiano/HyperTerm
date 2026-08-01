[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$runningProcesses = Get-Process -Name 'HyperTerm' -ErrorAction SilentlyContinue
if ($null -ne $runningProcesses) {
    throw 'Close every running HyperTerm window before resetting local data.'
}

$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$resolvedLocalApplicationData = [IO.Path]::GetFullPath(
    $localApplicationData).TrimEnd([IO.Path]::DirectorySeparatorChar)
$directoryNames = @('HyperTerm', 'hyperTerms', 'SuperTerminal')
$targetDirectories = foreach ($directoryName in $directoryNames) {
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $resolvedLocalApplicationData $directoryName))
    $candidateParent = [IO.Path]::GetDirectoryName($candidate).TrimEnd(
        [IO.Path]::DirectorySeparatorChar)

    if (-not $candidateParent.Equals(
            $resolvedLocalApplicationData,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe data path rejected: $candidate"
    }

    $candidate
}

$existingDirectories = @(
    $targetDirectories | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
)

if ($existingDirectories.Count -eq 0) {
    Write-Host 'No HyperTerm local data was found.'
    exit 0
}

Write-Host 'The following directories will be permanently deleted:'
$existingDirectories | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'This removes all sessions, folders, settings, and legacy data.'

if (-not $Force) {
    $confirmation = Read-Host 'Type DELETE to continue'
    if ($confirmation -cne 'DELETE') {
        Write-Host 'Reset cancelled. No data was deleted.'
        exit 0
    }
}

foreach ($directory in $existingDirectories) {
    $item = Get-Item -LiteralPath $directory -Force
    if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "Reparse point rejected: $directory"
    }

    Remove-Item -LiteralPath $directory -Recurse -Force
    Write-Host "Deleted: $directory"
}

Write-Host ''
Write-Host 'HyperTerm local data reset completed.'
Write-Host 'The PowerShell selector will open on the next launch.'
