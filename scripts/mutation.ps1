[CmdletBinding()]
param(
    [switch]$WebTerminal
)

$ErrorActionPreference = 'Stop'
$target = Join-Path (Split-Path -Parent $PSScriptRoot) 'eng\mutation.ps1'
& $target @PSBoundParameters
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
