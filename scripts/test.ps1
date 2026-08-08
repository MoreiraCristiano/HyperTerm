[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Filter,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'HyperTerm.sln'
$testArguments = @(
    'test',
    $solution,
    '--configuration',
    $Configuration,
    '--nologo'
)
if ($NoBuild) {
    $testArguments += '--no-build'
}
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $testArguments += @('--filter', $Filter)
}

& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
