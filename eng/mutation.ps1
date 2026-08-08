[CmdletBinding()]
param(
    [switch]$WebTerminal
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    & dotnet stryker --config-file stryker-config.json
    if ($LASTEXITCODE -ne 0) { throw '.NET mutation threshold failed.' }

    if ($WebTerminal) {
        & npm.cmd run test:mutation --prefix '.\src\HyperTerm.UI\WebTerminal'
        if ($LASTEXITCODE -ne 0) { throw 'Web terminal mutation threshold failed.' }
    }
}
finally {
    Pop-Location
}
