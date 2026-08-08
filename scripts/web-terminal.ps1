[CmdletBinding()]
param(
    [ValidateSet('Restore', 'Build', 'Test', 'Coverage', 'All')]
    [string]$Task = 'All'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webTerminal = Join-Path $repositoryRoot 'src\HyperTerm.UI\WebTerminal'

function Invoke-Npm {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & npm.cmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($Task -in @('Restore', 'All')) {
    Invoke-Npm -Arguments @('ci', '--prefix', $webTerminal, '--no-audit', '--no-fund')
}
if ($Task -in @('Test', 'All')) {
    Invoke-Npm -Arguments @('test', '--prefix', $webTerminal)
}
if ($Task -eq 'Coverage') {
    Invoke-Npm -Arguments @('run', 'test:coverage', '--prefix', $webTerminal)
}
if ($Task -in @('Build', 'All')) {
    Invoke-Npm -Arguments @('run', 'build', '--prefix', $webTerminal)
}
