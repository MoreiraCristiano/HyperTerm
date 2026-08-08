[CmdletBinding()]
param(
    [switch]$Package,

    [switch]$Coverage,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'HyperTerm.sln'
$webTerminalPath = Join-Path $repositoryRoot 'src\HyperTerm.UI\WebTerminal'

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

Push-Location $repositoryRoot
try {
    Invoke-CheckedCommand 'Restoring locked .NET dependencies...' {
        & dotnet restore $solutionPath --locked-mode --nologo
    }

    Invoke-CheckedCommand 'Restoring locked web terminal dependencies...' {
        & npm.cmd ci --prefix $webTerminalPath --no-audit --no-fund
    }

    Invoke-CheckedCommand 'Auditing .NET dependencies...' {
        & dotnet list $solutionPath package --vulnerable --include-transitive
    }

    Invoke-CheckedCommand 'Auditing web terminal dependencies...' {
        & npm.cmd audit --prefix $webTerminalPath --audit-level=high
    }

    Invoke-CheckedCommand 'Verifying .NET formatting...' {
        & dotnet format $solutionPath --verify-no-changes --no-restore
    }

    Invoke-CheckedCommand 'Building web terminal...' {
        & npm.cmd run build --prefix $webTerminalPath
    }

    Invoke-CheckedCommand 'Testing web terminal...' {
        if ($Coverage) {
            & npm.cmd run test:coverage --prefix $webTerminalPath
        }
        else {
            & npm.cmd test --prefix $webTerminalPath
        }
    }

    Invoke-CheckedCommand 'Building HyperTerm in Release mode...' {
        & dotnet build $solutionPath --configuration Release --no-restore --nologo
    }

    if ($Coverage) {
        Invoke-CheckedCommand 'Restoring repository tools...' {
            & dotnet tool restore
        }

        Invoke-CheckedCommand 'Running HyperTerm tests with coverage...' {
            & (Join-Path $repositoryRoot 'eng\coverage.ps1') -Enforce
        }
    }
    else {
        Invoke-CheckedCommand 'Running HyperTerm tests...' {
            & dotnet test $solutionPath `
                --configuration Release `
                --no-build `
                --no-restore `
                --nologo
        }
    }

    if ($Package) {
        & (Join-Path $repositoryRoot 'build.ps1') -Runtime $Runtime -Version $Version
        if ($LASTEXITCODE -ne 0) {
            throw "Release package validation failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host 'HyperTerm verification completed successfully.'
}
finally {
    Pop-Location
}
